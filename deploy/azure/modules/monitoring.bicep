// Log Analytics workspace (Container Apps console logs) and workspace-based Application Insights (ADR-0008: the API and
// Worker export requests, dependencies and Serilog events through Azure Monitor OpenTelemetry).

param location string
param tags object
param workspaceName string
param appInsightsName string

@description('Days of log retention (30 is the free minimum).')
param retentionDays int

@description('Daily ingestion cap in GB; -1 for none. Keeps a chatty bug from running up the bill.')
param dailyCapGb int

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-07-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionDays
    workspaceCapping: {
      dailyQuotaGb: dailyCapGb
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
    RetentionInDays: retentionDays
  }
}

output workspaceName string = workspace.name
output appInsightsName string = appInsights.name
