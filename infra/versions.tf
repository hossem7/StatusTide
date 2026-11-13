terraform {
  # 1.10 added S3 lockfile locking, so no DynamoDB table is needed for locks.
  required_version = ">= 1.10"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.71"
    }
  }

  # Partial backend config: the bucket name holds the account id, so it lives in
  # backend.hcl (not committed). Run `terraform init -backend-config=backend.hcl`.
  backend "s3" {
    key          = "statustide/main.tfstate"
    encrypt      = true
    use_lockfile = true
  }
}

locals {
  default_tags = {
    Project   = "StatusTide"
    Stack     = "main"
    ManagedBy = "Terraform"
  }
}

provider "aws" {
  region = var.region

  default_tags {
    tags = local.default_tags
  }
}

# CloudFront only accepts certificates from us-east-1, whatever the main region is.
provider "aws" {
  alias  = "us_east_1"
  region = "us-east-1"

  default_tags {
    tags = local.default_tags
  }
}
