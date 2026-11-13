variable "region" {
  description = "Main AWS region. Must offer Amazon Nova Lite on Bedrock and SES."
  type        = string
  default     = "us-east-1"
}

variable "domain" {
  description = "Domain registered in Route 53, such as statustide.example. Its hosted zone must already exist."
  type        = string
}

variable "instance_type" {
  description = "EC2 instance type for the k3s server. 2 GB of memory is the minimum for the whole stack."
  type        = string
  default     = "t3.small"
}

variable "root_volume_size" {
  description = "Root disk size in GiB. Holds the OS, container images and the PostgreSQL data."
  type        = number
  default     = 20
}

variable "k3s_version" {
  description = "k3s release to install, pinned so a rebuild gets the same cluster."
  type        = string
  default     = "v1.37.1+k3s1"
}

variable "backups_bucket" {
  description = "Name of the backup bucket made by infra/bootstrap (its backups_bucket output)."
  type        = string
}

variable "bedrock_model_id" {
  description = "Bedrock model the summarizer calls."
  type        = string
  default     = "amazon.nova-lite-v1:0"
}

variable "log_retention_days" {
  description = "How long CloudWatch keeps app logs."
  type        = number
  default     = 14
}

variable "monthly_budget_usd" {
  description = "Monthly spend limit. At 100% of actual spend the server is stopped."
  type        = number
  default     = 30
}

variable "alert_email" {
  description = "Address that gets budget warnings. Kept out of the repo in terraform.tfvars."
  type        = string

  # Plans are posted on public pull requests, so the address is hidden in them.
  sensitive = true
}
