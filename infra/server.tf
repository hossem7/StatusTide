# One EC2 instance running k3s. The default VPC is enough for a single public
# server, and it avoids a NAT gateway (about $32 a month on its own).

data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }

  filter {
    name   = "default-for-az"
    values = ["true"]
  }
}

# Ubuntu 24.04 LTS, looked up from Canonical's public parameter.
data "aws_ssm_parameter" "ubuntu_ami" {
  name = "/aws/service/canonical/ubuntu/server/24.04/stable/current/amd64/hvm/ebs-gp3/ami-id"
}

# CloudFront reaches the server only from its origin-facing addresses.
data "aws_ec2_managed_prefix_list" "cloudfront_origin" {
  name = "com.amazonaws.global.cloudfront.origin-facing"
}

resource "aws_security_group" "server" {
  name        = "statustide-server"
  description = "HTTP from CloudFront only. No SSH: shell access goes through SSM Session Manager."
  vpc_id      = data.aws_vpc.default.id
}

resource "aws_vpc_security_group_ingress_rule" "http_from_cloudfront" {
  security_group_id = aws_security_group.server.id
  description       = "Traefik, reached only by CloudFront"
  prefix_list_id    = data.aws_ec2_managed_prefix_list.cloudfront_origin.id
  ip_protocol       = "tcp"
  from_port         = 80
  to_port           = 80
}

# The server polls vendor feeds, pulls images and calls AWS APIs.
resource "aws_vpc_security_group_egress_rule" "all_ipv4" {
  security_group_id = aws_security_group.server.id
  description       = "Outbound to the internet"
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "-1"
}

resource "aws_instance" "server" {
  ami                  = data.aws_ssm_parameter.ubuntu_ami.insecure_value
  instance_type        = var.instance_type
  subnet_id            = sort(data.aws_subnets.default.ids)[0]
  iam_instance_profile = aws_iam_instance_profile.server.name

  vpc_security_group_ids = [aws_security_group.server.id]

  # "standard" means a traffic spike slows the server down instead of adding to
  # the bill, which "unlimited" (the t3 default) would do.
  credit_specification {
    cpu_credits = "standard"
  }

  # IMDSv2 only, so a server-side request forgery bug cannot read the role's
  # credentials with a plain GET. Hop limit 2 lets pods (one extra network hop)
  # reach the metadata service for the role, which the AWS SDKs need.
  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required"
    http_put_response_hop_limit = 2
  }

  root_block_device {
    volume_type = "gp3"
    volume_size = var.root_volume_size
    encrypted   = true
  }

  user_data = templatefile("${path.module}/server-init.sh.tftpl", {
    k3s_version = var.k3s_version
  })

  # A new Ubuntu image every few weeks would otherwise replace the server, and
  # with it the database. Rebuilds are a deliberate `terraform apply -replace`.
  lifecycle {
    ignore_changes = [ami, user_data]
  }

  tags = {
    Name = "statustide-server"
  }
}

# A fixed public address, so CloudFront's origin name survives stop and start.
resource "aws_eip" "server" {
  domain   = "vpc"
  instance = aws_instance.server.id

  tags = {
    Name = "statustide-server"
  }
}
