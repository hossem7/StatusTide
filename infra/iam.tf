# The server's role. Apps on the instance get temporary credentials from it,
# so no AWS access key exists anywhere. Each statement grants only what an app
# feature needs, scoped to StatusTide's own resources.

data "aws_caller_identity" "current" {}
data "aws_partition" "current" {}

data "aws_iam_policy_document" "server_trust" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ec2.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "server" {
  name               = "statustide-server"
  description        = "Role of the StatusTide EC2 server."
  assume_role_policy = data.aws_iam_policy_document.server_trust.json
}

resource "aws_iam_instance_profile" "server" {
  name = "statustide-server"
  role = aws_iam_role.server.name
}

# Shell access through SSM Session Manager instead of SSH, so port 22 stays closed
# and there is no key pair to lose.
resource "aws_iam_role_policy_attachment" "server_ssm" {
  role       = aws_iam_role.server.name
  policy_arn = "arn:${data.aws_partition.current.partition}:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

data "aws_s3_bucket" "backups" {
  bucket = var.backups_bucket
}

data "aws_iam_policy_document" "server" {
  # Daily pg_dump uploads, and downloads to restore after a rebuild.
  statement {
    sid       = "ListBackups"
    actions   = ["s3:ListBucket"]
    resources = [data.aws_s3_bucket.backups.arn]
  }

  statement {
    sid       = "ReadWriteBackups"
    actions   = ["s3:GetObject", "s3:PutObject"]
    resources = ["${data.aws_s3_bucket.backups.arn}/*"]
  }

  # Alert and confirmation emails, only from our domain identity.
  statement {
    sid     = "SendEmail"
    actions = ["ses:SendEmail", "ses:SendRawEmail"]
    resources = [
      aws_sesv2_email_identity.domain.arn,
      aws_sesv2_configuration_set.main.arn,
    ]
  }

  # Incident summaries. The model id may name a cross-region inference profile
  # (us.amazon.nova-lite-v1:0), which then calls the model in several regions.
  statement {
    sid     = "InvokeSummaryModel"
    actions = ["bedrock:InvokeModel"]
    resources = [
      "arn:${data.aws_partition.current.partition}:bedrock:*::foundation-model/${trimprefix(var.bedrock_model_id, "us.")}",
      "arn:${data.aws_partition.current.partition}:bedrock:${var.region}:${data.aws_caller_identity.current.account_id}:inference-profile/${var.bedrock_model_id}",
    ]
  }

  # Serilog writes straight to these log groups.
  statement {
    sid       = "WriteLogs"
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents", "logs:DescribeLogStreams"]
    resources = [for group in aws_cloudwatch_log_group.app : "${group.arn}:*"]
  }
}

resource "aws_iam_role_policy" "server" {
  name   = "statustide-app"
  role   = aws_iam_role.server.id
  policy = data.aws_iam_policy_document.server.json
}
