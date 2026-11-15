terraform {
  required_version = ">= 1.5.0"
}

variable "tfc_organization" {
  description = "Organização no HCP Terraform."
  type        = string
}

variable "infra_workspace" {
  description = "Workspace do repositório infra-k8s (cluster EKS)."
  type        = string
}

variable "ecr_workspace" {
  description = "Workspace do stack ecr/ do repositório infra-k8s."
  type        = string
}

variable "mensageria_workspace" {
  description = "Workspace do stack mensageria/ do infra-k8s (SNS, SQS e role IRSA)."
  type        = string
}

variable "dynamodb_workspace" {
  description = "Workspace do stack dynamodb-execucao/ do infra-k8s."
  type        = string
}

data "terraform_remote_state" "infra" {
  backend = "remote"

  config = {
    organization = var.tfc_organization
    workspaces = {
      name = var.infra_workspace
    }
  }
}

data "terraform_remote_state" "ecr" {
  backend = "remote"

  config = {
    organization = var.tfc_organization
    workspaces = {
      name = var.ecr_workspace
    }
  }
}

data "terraform_remote_state" "mensageria" {
  backend = "remote"

  config = {
    organization = var.tfc_organization
    workspaces = {
      name = var.mensageria_workspace
    }
  }
}

data "terraform_remote_state" "dynamodb" {
  backend = "remote"

  config = {
    organization = var.tfc_organization
    workspaces = {
      name = var.dynamodb_workspace
    }
  }
}

output "cluster_name" {
  value = data.terraform_remote_state.infra.outputs.cluster_name
}

output "api_repository" {
  value = data.terraform_remote_state.ecr.outputs.execucao_repository_url
}

output "chart_repository" {
  value = data.terraform_remote_state.ecr.outputs.execucao_chart_repository
}

output "chart_repository_url" {
  value = data.terraform_remote_state.ecr.outputs.execucao_chart_repository_url
}

output "role_arn" {
  value = data.terraform_remote_state.mensageria.outputs.execucao_role_arn
}

output "topic_arn" {
  value = data.terraform_remote_state.mensageria.outputs.execucao_topic_arn
}

output "queue_url" {
  value = data.terraform_remote_state.mensageria.outputs.execucao_queue_url
}

output "tabela_execucao" {
  value = data.terraform_remote_state.dynamodb.outputs.execucao_tabelas.execucao
}

output "tabela_outbox" {
  value = data.terraform_remote_state.dynamodb.outputs.execucao_tabelas.outbox
}

output "tabela_idempotencia" {
  value = data.terraform_remote_state.dynamodb.outputs.execucao_tabelas.idempotencia
}
