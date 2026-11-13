# --- Logs ---
# One group per app. Serilog creates the streams; Terraform owns the groups so
# retention is set and `terraform destroy` removes them.

resource "aws_cloudwatch_log_group" "app" {
  for_each = toset(["api", "publisher", "subscriber"])

  name              = "/statustide/${each.key}"
  retention_in_days = var.log_retention_days
}

# --- Budget ---
# Email at 80% of the forecast and at 100% of actual spend. At 100% of actual
# spend AWS also stops the server, so a runaway month cannot keep billing for
# compute. Stopped storage and the Elastic IP still cost a little.

resource "aws_budgets_budget" "monthly" {
  name         = "statustide-monthly"
  budget_type  = "COST"
  limit_amount = tostring(var.monthly_budget_usd)
  limit_unit   = "USD"
  time_unit    = "MONTHLY"

  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 80
    threshold_type             = "PERCENTAGE"
    notification_type          = "FORECASTED"
    subscriber_email_addresses = [var.alert_email]
  }

  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 100
    threshold_type             = "PERCENTAGE"
    notification_type          = "ACTUAL"
    subscriber_email_addresses = [var.alert_email]
  }
}

data "aws_iam_policy_document" "budget_action_trust" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["budgets.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "aws:SourceAccount"
      values   = [data.aws_caller_identity.current.account_id]
    }
  }
}

resource "aws_iam_role" "budget_action" {
  name               = "statustide-budget-action"
  description        = "Lets AWS Budgets stop the StatusTide server."
  assume_role_policy = data.aws_iam_policy_document.budget_action_trust.json
}

# AWS's own policy for budget actions that run SSM documents. It allows stopping
# instances only through SSM, and only Budgets in this account can use the role.
resource "aws_iam_role_policy_attachment" "budget_action" {
  role       = aws_iam_role.budget_action.name
  policy_arn = "arn:${data.aws_partition.current.partition}:iam::aws:policy/AWSBudgetsActions_RolePolicyForResourceAdministrationWithSSM"
}

resource "aws_budgets_budget_action" "stop_server" {
  budget_name        = aws_budgets_budget.monthly.name
  action_type        = "RUN_SSM_DOCUMENTS"
  approval_model     = "AUTOMATIC"
  notification_type  = "ACTUAL"
  execution_role_arn = aws_iam_role.budget_action.arn

  action_threshold {
    action_threshold_type  = "PERCENTAGE"
    action_threshold_value = 100
  }

  definition {
    ssm_action_definition {
      action_sub_type = "STOP_EC2_INSTANCES"
      region          = var.region
      instance_ids    = [aws_instance.server.id]
    }
  }

  subscriber {
    address           = var.alert_email
    subscription_type = "EMAIL"
  }
}
