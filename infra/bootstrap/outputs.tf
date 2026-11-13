output "state_bucket" {
  description = "Put this in infra/backend.hcl as the bucket."
  value       = aws_s3_bucket.state.id
}

output "backups_bucket" {
  description = "Put this in infra/terraform.tfvars as backups_bucket."
  value       = aws_s3_bucket.backups.id
}

output "plan_role_arn" {
  description = "Save this as the AWS_PLAN_ROLE_ARN repository variable in GitHub to turn on plans in CI."
  value       = aws_iam_role.plan.arn
}
