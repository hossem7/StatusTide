terraform {
  required_version = ">= 1.10"

  # Bootstrap creates the state bucket, so it cannot keep its own state there.
  # Its state stays on the machine that applies it. It changes rarely and every
  # resource here can be imported again if the file is lost.

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.71"
    }
  }
}

provider "aws" {
  region = var.region

  default_tags {
    tags = {
      Project   = "StatusTide"
      Stack     = "bootstrap"
      ManagedBy = "Terraform"
    }
  }
}
