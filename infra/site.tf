# The React build is served from a private S3 bucket through CloudFront.
# /api/* and /hubs/* go to the EC2 server instead.

resource "aws_s3_bucket" "web" {
  bucket = "statustide-web-${data.aws_caller_identity.current.account_id}"

  # The bucket only holds build output, which CI uploads again on every deploy.
  force_destroy = true
}

resource "aws_s3_bucket_public_access_block" "web" {
  bucket = aws_s3_bucket.web.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "web" {
  bucket = aws_s3_bucket.web.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_cloudfront_origin_access_control" "web" {
  name                              = "statustide-web"
  description                       = "CloudFront signs its requests to the web bucket."
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

# Only this distribution can read the bucket.
data "aws_iam_policy_document" "web_bucket" {
  statement {
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.web.arn}/*"]

    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.site.arn]
    }
  }
}

resource "aws_s3_bucket_policy" "web" {
  bucket = aws_s3_bucket.web.id
  policy = data.aws_iam_policy_document.web_bucket.json
}

# App paths such as /incidents/{id} have no file in the bucket. This sends them
# to index.html. A distribution-wide 404 rule would also rewrite the API's own
# 404 answers, which the app relies on, so the rewrite runs on the S3 path only.
resource "aws_cloudfront_function" "spa_routes" {
  name    = "statustide-spa-routes"
  runtime = "cloudfront-js-2.0"
  comment = "Serve index.html for app paths that are not files."
  publish = true
  code    = file("${path.module}/spa-routes.js")
}

data "aws_cloudfront_cache_policy" "caching_optimized" {
  name = "Managed-CachingOptimized"
}

data "aws_cloudfront_cache_policy" "caching_disabled" {
  name = "Managed-CachingDisabled"
}

data "aws_cloudfront_response_headers_policy" "security_headers" {
  name = "Managed-SecurityHeadersPolicy"
}

# API requests pass through untouched: every header (including Authorization,
# which CloudFront forwards when caching is off), cookies and query strings.
# CloudFront-Viewer-Address carries the visitor's real address, since the
# X-Forwarded-For entry nearest the server is a CloudFront edge.
resource "aws_cloudfront_origin_request_policy" "api" {
  name    = "statustide-api"
  comment = "Forward everything the API needs, plus the visitor address."

  cookies_config {
    cookie_behavior = "all"
  }

  headers_config {
    header_behavior = "allViewerAndWhitelistCloudFront"

    headers {
      items = ["CloudFront-Viewer-Address"]
    }
  }

  query_strings_config {
    query_string_behavior = "all"
  }
}

locals {
  web_origin_id = "web-bucket"
  api_origin_id = "api-server"
}

resource "aws_cloudfront_distribution" "site" {
  enabled             = true
  is_ipv6_enabled     = true
  http_version        = "http2and3"
  comment             = "StatusTide"
  aliases             = [var.domain]
  default_root_object = "index.html"

  # North America and Europe only, the cheapest price class.
  price_class = "PriceClass_100"

  origin {
    origin_id                = local.web_origin_id
    domain_name              = aws_s3_bucket.web.bucket_regional_domain_name
    origin_access_control_id = aws_cloudfront_origin_access_control.web.id
  }

  # CloudFront needs a host name, not an IP. The Elastic IP's public DNS name
  # stays the same for as long as the address is kept.
  origin {
    origin_id   = local.api_origin_id
    domain_name = aws_eip.server.public_dns

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "http-only"
      origin_ssl_protocols   = ["TLSv1.2"]

      # SignalR keeps WebSockets open; CloudFront's idle limit is 60 s by
      # default, and the hub pings every 15 s, so the default is enough.
      origin_read_timeout = 60
    }
  }

  default_cache_behavior {
    target_origin_id           = local.web_origin_id
    viewer_protocol_policy     = "redirect-to-https"
    allowed_methods            = ["GET", "HEAD"]
    cached_methods             = ["GET", "HEAD"]
    compress                   = true
    cache_policy_id            = data.aws_cloudfront_cache_policy.caching_optimized.id
    response_headers_policy_id = data.aws_cloudfront_response_headers_policy.security_headers.id

    function_association {
      event_type   = "viewer-request"
      function_arn = aws_cloudfront_function.spa_routes.arn
    }
  }

  dynamic "ordered_cache_behavior" {
    for_each = ["/api/*", "/hubs/*"]

    content {
      path_pattern               = ordered_cache_behavior.value
      target_origin_id           = local.api_origin_id
      viewer_protocol_policy     = "redirect-to-https"
      allowed_methods            = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
      cached_methods             = ["GET", "HEAD"]
      compress                   = true
      cache_policy_id            = data.aws_cloudfront_cache_policy.caching_disabled.id
      origin_request_policy_id   = aws_cloudfront_origin_request_policy.api.id
      response_headers_policy_id = data.aws_cloudfront_response_headers_policy.security_headers.id
    }
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    acm_certificate_arn      = aws_acm_certificate_validation.site.certificate_arn
    ssl_support_method       = "sni-only"
    minimum_protocol_version = "TLSv1.2_2021"
  }
}
