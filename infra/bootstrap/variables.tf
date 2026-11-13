variable "region" {
  description = "AWS region for the state and backup buckets. Use the same region as the main stack."
  type        = string
  default     = "us-east-1"
}

variable "github_repository" {
  description = "Repository allowed to assume the plan role, as owner/name."
  type        = string
  default     = "hossem7/StatusTide"
}

variable "create_github_oidc_provider" {
  description = "Set to false if the account already has the GitHub OIDC provider. An account can hold only one provider per URL."
  type        = bool
  default     = true
}

variable "backup_retention_days" {
  description = "How long database backups are kept before S3 deletes them."
  type        = number
  default     = 30
}
