# StatusTide infrastructure

Every AWS resource StatusTide uses is defined here in Terraform. Nothing is created by clicking through the console, except the one-time steps in [#20](https://github.com/hossem7/StatusTide/issues/20): the account, the spend limit, the domain registration and the SES production access request.

There are two stacks:

| Stack | Holds | Lifetime |
| --- | --- | --- |
| `bootstrap/` | Terraform state bucket, database backup bucket, the read-only role CI uses to plan | Applied once, kept forever |
| `infra/` (this folder) | Everything that runs the site: EC2 server, IAM role, CloudFront, certificate, DNS records, web bucket, SES, log groups, budget | Destroyed between job searches, rebuilt with one apply |

The split is what makes `terraform destroy` safe. The main stack holds nothing that can't be rebuilt: the data lives in the backups bucket, the domain and its hosted zone come from the registration, and state lives in the state bucket.

## What gets built

```
visitor ── https ──> CloudFront ──┬── /*               ──> S3 web bucket (React build, private, read through OAC)
         (Route 53 alias,          └── /api/*, /hubs/* ──> EC2 t3.small, port 80 (k3s + Traefik)
          ACM certificate)                                   only CloudFront's address ranges may connect
```

- **Server:** one t3.small (2 GB) on Ubuntu 24.04 in the default VPC, with an Elastic IP, a 20 GB encrypted gp3 disk and 2 GB of swap. k3s is installed at first boot (`server-init.sh.tftpl`). CPU credits are "standard", so a spike slows the server down instead of adding to the bill.
- **No SSH.** Port 22 is closed and there is no key pair. Open a shell with `aws ssm start-session --target <server_instance_id>`.
- **IMDSv2 only,** with a hop limit of 2 so pods can still get the role's credentials. The app's SSRF guard blocks 169.254.169.254 as well.
- **IAM role:** read and write on the backups bucket, send email from our SES identity, invoke the Nova Lite model, write to our log groups, and SSM Session Manager. No access keys anywhere.
- **Frontend routes:** a CloudFront Function rewrites paths without a file extension (`/incidents/{id}`) to `/index.html`. It runs on the S3 path only, so the API's own 404 answers reach the app unchanged.
- **Visitor address:** API requests carry `CloudFront-Viewer-Address`, since the `X-Forwarded-For` entry nearest the server is a CloudFront edge. Reading it in `ClientAddress` is follow-up work.
- **Email:** SES domain identity with DKIM, a custom MAIL FROM domain (`mail.<domain>`) with SPF, and DMARC at `p=none` to start. Bounce, complaint and delivery events go to an SNS topic for [#18](https://github.com/hossem7/StatusTide/issues/18).
- **Logs:** `/statustide/api`, `/statustide/publisher` and `/statustide/subscriber`, kept 14 days.
- **Budget:** $30 a month. Email at 80% of the forecast; at 100% of actual spend, AWS Budgets stops the server.

Known trade-off: traffic between CloudFront and the server is plain HTTP over AWS's network, limited to CloudFront's origin-facing addresses. HTTPS to the origin needs a certificate on the server, which is a later improvement.

Rough monthly cost: EC2 t3.small about $15, public IPv4 about $3.60, EBS 20 GB about $1.60, Route 53 zone $0.50, everything else a few cents at portfolio traffic.

## Requirements

- Terraform 1.10 or later (CI uses 1.17.1). Without a local install: `docker run --rm -v "$PWD:/w" -w /w hashicorp/terraform:1.17.1 <command>`.
- AWS CLI signed in as an administrator, through IAM Identity Center (`aws sso login`). Never create access keys.
- The domain registered in Route 53, which creates its hosted zone.

## First setup

1. **Bootstrap** (once per account):

   ```bash
   cd infra/bootstrap
   terraform init
   terraform apply
   ```

   If the account already has a GitHub OIDC provider, add `-var create_github_oidc_provider=false`. Bootstrap keeps its state on your machine (`terraform.tfstate`, not committed). Keep a copy somewhere safe; if it is lost, `terraform import` brings the resources back.

2. **Configure the main stack:**

   ```bash
   cd infra
   cp backend.hcl.example backend.hcl            # bucket = state_bucket output
   cp terraform.tfvars.example terraform.tfvars  # domain, backups_bucket, alert_email
   terraform init -backend-config=backend.hcl
   ```

3. **Apply:**

   ```bash
   terraform plan -out=main.tfplan
   terraform apply main.tfplan
   ```

   The certificate waits for DNS validation, and a new CloudFront distribution takes several minutes, so the first apply takes about 10 to 15 minutes. Confirm the budget emails' subscription when AWS sends it.

4. **Plans in CI:** in the GitHub repository settings, add these under Secrets and variables, Actions:

   | Kind | Name | Value |
   | --- | --- | --- |
   | Variable | `AWS_PLAN_ROLE_ARN` | bootstrap's `plan_role_arn` output |
   | Variable | `AWS_REGION` | `us-east-1` |
   | Variable | `TF_STATE_BUCKET` | bootstrap's `state_bucket` output |
   | Variable | `TF_BACKUPS_BUCKET` | bootstrap's `backups_bucket` output |
   | Variable | `STATUSTIDE_DOMAIN` | the domain |
   | Secret | `BUDGET_ALERT_EMAIL` | the budget email address |

   Until `AWS_PLAN_ROLE_ARN` exists, the plan job is skipped. Plans are posted as a comment on the pull request. The repository is public, so the plan shows the account id in resource ARNs; AWS does not treat account ids as secret, and the alert email is hidden as a sensitive value.

Deploying the app onto k3s is covered in [#20](https://github.com/hossem7/StatusTide/issues/20).

## Day to day

Change the `.tf` files in a pull request. CI runs `terraform fmt -check`, `terraform validate` and tflint on both stacks, then posts a plan. After merging, apply by hand from `main`:

```bash
git switch main && git pull
cd infra && terraform plan -out=main.tfplan && terraform apply main.tfplan
```

CI never applies. Applies need administrator access, and the CI role is read only.

A new Ubuntu image does not replace the server, since that would also wipe the database: `ami` and `user_data` changes are ignored. To rebuild the server on purpose, back up the database first, then run `terraform apply -replace=aws_instance.server`.

## Tear down and rebuild

Between job searches:

```bash
# 1. Take a final database backup to the backups bucket (see #20 for the command).
# 2. Remove everything in the main stack.
cd infra && terraform destroy
```

Kept: the domain and hosted zone, the state bucket, the backups and the CI role. Deleted: the server and its disk, CloudFront, the certificate, the web bucket, SES identity, log groups and the budget.

To rebuild, run `terraform apply` again, deploy the app (#20) and restore the latest backup. A fresh SES identity has to verify DKIM again, which takes a few minutes once the records exist. SES production access belongs to the account, so it survives a rebuild.
