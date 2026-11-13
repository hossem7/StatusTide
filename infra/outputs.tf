output "site_url" {
  value = "https://${var.domain}"
}

output "server_instance_id" {
  description = "Open a shell with: aws ssm start-session --target <id>"
  value       = aws_instance.server.id
}

output "server_public_dns" {
  value = aws_eip.server.public_dns
}

output "web_bucket" {
  description = "Where the deploy workflow uploads the React build."
  value       = aws_s3_bucket.web.id
}

output "cloudfront_distribution_id" {
  description = "Needed to invalidate the cache after a frontend deploy."
  value       = aws_cloudfront_distribution.site.id
}

output "email_events_topic_arn" {
  description = "SES bounce, complaint and delivery events (#18 subscribes to this)."
  value       = aws_sns_topic.email_events.arn
}

output "ses_configuration_set" {
  value = aws_sesv2_configuration_set.main.configuration_set_name
}
