# SES sends alert and confirmation emails from the domain. DKIM signs them,
# SPF covers the custom MAIL FROM domain, and DMARC tells receivers what to do
# with mail that fails both. Production access is requested by hand (#20).

resource "aws_sesv2_configuration_set" "main" {
  configuration_set_name = "statustide"

  reputation_options {
    reputation_metrics_enabled = true
  }

  # Addresses that bounce or complain are blocked for the whole account.
  suppression_options {
    suppressed_reasons = ["BOUNCE", "COMPLAINT"]
  }
}

resource "aws_sesv2_email_identity" "domain" {
  email_identity         = var.domain
  configuration_set_name = aws_sesv2_configuration_set.main.configuration_set_name

  dkim_signing_attributes {
    next_signing_key_length = "RSA_2048_BIT"
  }
}

resource "aws_route53_record" "dkim" {
  count = 3

  zone_id = data.aws_route53_zone.main.zone_id
  name    = "${aws_sesv2_email_identity.domain.dkim_signing_attributes[0].tokens[count.index]}._domainkey.${var.domain}"
  type    = "CNAME"
  ttl     = 1800
  records = ["${aws_sesv2_email_identity.domain.dkim_signing_attributes[0].tokens[count.index]}.dkim.amazonses.com"]
}

# Bounces come back to mail.<domain>, so SPF passes for our own domain rather
# than amazonses.com, which DMARC alignment needs.
locals {
  mail_from_domain = "mail.${var.domain}"
}

resource "aws_sesv2_email_identity_mail_from_attributes" "domain" {
  email_identity         = aws_sesv2_email_identity.domain.email_identity
  mail_from_domain       = local.mail_from_domain
  behavior_on_mx_failure = "USE_DEFAULT_VALUE"
}

resource "aws_route53_record" "mail_from_mx" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = local.mail_from_domain
  type    = "MX"
  ttl     = 1800
  records = ["10 feedback-smtp.${var.region}.amazonses.com"]
}

resource "aws_route53_record" "mail_from_spf" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = local.mail_from_domain
  type    = "TXT"
  ttl     = 1800
  records = ["v=spf1 include:amazonses.com -all"]
}

# Start with p=none to collect reports without blocking anything. Move to
# quarantine once the reports show only SES sending for the domain.
resource "aws_route53_record" "dmarc" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = "_dmarc.${var.domain}"
  type    = "TXT"
  ttl     = 1800
  records = ["v=DMARC1; p=none; adkim=s; aspf=s"]
}

# Bounce, complaint and delivery events go to an SNS topic. #18 subscribes to it
# to suppress bad addresses and record each alert's delivery status.
resource "aws_sns_topic" "email_events" {
  name = "statustide-email-events"
}

data "aws_iam_policy_document" "email_events" {
  statement {
    actions   = ["sns:Publish"]
    resources = [aws_sns_topic.email_events.arn]

    principals {
      type        = "Service"
      identifiers = ["ses.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "AWS:SourceAccount"
      values   = [data.aws_caller_identity.current.account_id]
    }
  }
}

resource "aws_sns_topic_policy" "email_events" {
  arn    = aws_sns_topic.email_events.arn
  policy = data.aws_iam_policy_document.email_events.json
}

resource "aws_sesv2_configuration_set_event_destination" "email_events" {
  configuration_set_name = aws_sesv2_configuration_set.main.configuration_set_name
  event_destination_name = "sns"

  event_destination {
    enabled              = true
    matching_event_types = ["BOUNCE", "COMPLAINT", "DELIVERY"]

    sns_destination {
      topic_arn = aws_sns_topic.email_events.arn
    }
  }

  depends_on = [aws_sns_topic_policy.email_events]
}
