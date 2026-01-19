@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short prefix used to build resource names (3-12 chars, lowercase letters/numbers recommended).')
param namePrefix string

@description('Name of the intake table used for work discovery.')
param intakeTableName string = 'Intake'

@description('Cosmos DB database name.')
param cosmosDatabaseName string = 'workflow'

@description('Cosmos DB container for append-only events.')
param cosmosEventsContainerName string = 'events'

@description('Cosmos DB container for query-optimized request projections.')
param cosmosProjectionsContainerName string = 'requests'

@description('Cosmos DB container for idempotency markers.')
param cosmosIdempotencyContainerName string = 'idempotency'

@description('Base URL of the external service API (deployed separately).')
param externalServiceBaseUrl string

@description('App ID URI (audience) of the external service API used for Managed Identity auth (e.g., api://externalservice).')
param externalServiceAudience string

@description('Timer schedule for discovery (CRON).')
param discoverSchedule string = '0 */5 * * * *'

@description('Timer schedule for polling scheduler (CRON).')
param pollSchedule string = '0 */5 * * * *'

@description('Whether to deploy Application Insights. Disable if you prefer OpenTelemetry-only or already have a workspace.')
param enableAppInsights bool = true

var suffix = uniqueString(resourceGroup().id, namePrefix)

var storageAccountName = toLower('${namePrefix}st${substring(suffix, 0, 8)}')
var cosmosAccountName = toLower('${namePrefix}cos${substring(suffix, 0, 8)}')
var eventGridTopicName = toLower('${namePrefix}eg${substring(suffix, 0, 8)}')
var functionAppName = toLower('${namePrefix}fn${substring(suffix, 0, 8)}')
var appInsightsName = toLower('${namePrefix}ai${substring(suffix, 0, 8)}')

// ---------------------------
// Storage Account (Tables)
// ---------------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-01-01' = {
  parent: storage
  name: 'default'
}

resource intakeTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  parent: tableService
  name: intakeTableName
}

// ---------------------------
// Cosmos DB (NoSQL)
// ---------------------------
resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: cosmosAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
      }
    ]
    enableAutomaticFailover: false
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
  }
}

resource cosmosDb 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmos
  name: cosmosDatabaseName
  properties: {
    resource: {
      id: cosmosDatabaseName
    }
  }
}

resource eventsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: cosmosDb
  name: cosmosEventsContainerName
  properties: {
    resource: {
      id: cosmosEventsContainerName
      partitionKey: {
        paths: [
          '/aggregateId'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
      }
    }
  }
}

resource projectionsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: cosmosDb
  name: cosmosProjectionsContainerName
  properties: {
    resource: {
      id: cosmosProjectionsContainerName
      partitionKey: {
        paths: [
          '/requestId'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
      }
    }
  }
}

resource idempotencyContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: cosmosDb
  name: cosmosIdempotencyContainerName
  properties: {
    resource: {
      id: cosmosIdempotencyContainerName
      partitionKey: {
        paths: [
          '/handler'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
      }
    }
  }
}

// ---------------------------
// Event Grid custom topic
// ---------------------------
resource topic 'Microsoft.EventGrid/topics@2024-06-01-preview' = {
  name: eventGridTopicName
  location: location
  properties: {
    inputSchema: 'CloudEventSchemaV1_0'
    publicNetworkAccess: 'Enabled'
  }
}

// ---------------------------
// Function App (Linux Consumption) + Identity
// ---------------------------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${functionAppName}-plan'
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  kind: 'functionapp'
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = if (enableAppInsights) {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

var storageKey = listKeys(storage.id, '2023-01-01').keys[0].value
var functionStorageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storageKey};EndpointSuffix=${environment().suffixes.storage}'

resource func 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: functionStorageConnectionString
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'DiscoverSchedule'
          value: discoverSchedule
        }
        {
          name: 'PollSchedule'
          value: pollSchedule
        }
        // Infrastructure-backed options (bound by the .NET configuration binder)
        {
          name: 'Table__AccountUri'
          value: 'https://${storage.name}.table.core.windows.net'
        }
        {
          name: 'Table__TableName'
          value: intakeTableName
        }
        {
          name: 'Cosmos__AccountEndpoint'
          value: cosmos.properties.documentEndpoint
        }
        {
          name: 'Cosmos__DatabaseName'
          value: cosmosDatabaseName
        }
        {
          name: 'Cosmos__EventsContainerName'
          value: cosmosEventsContainerName
        }
        {
          name: 'Cosmos__ProjectionsContainerName'
          value: cosmosProjectionsContainerName
        }
        {
          name: 'Cosmos__IdempotencyContainerName'
          value: cosmosIdempotencyContainerName
        }
        {
          name: 'EventGrid__TopicEndpoint'
          value: topic.properties.endpoint
        }
        {
          name: 'ExternalService__BaseUrl'
          value: externalServiceBaseUrl
        }
        {
          name: 'ExternalService__RequireAuth'
          value: 'true'
        }
        {
          name: 'ExternalService__Audience'
          value: externalServiceAudience
        }
        // Optional App Insights
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: enableAppInsights ? appInsights.properties.InstrumentationKey : ''
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: enableAppInsights ? appInsights.properties.ConnectionString : ''
        }
      ]
    }
  }
}

// ---------------------------
// Event Grid subscriptions (Topic -> Azure Functions)
// ---------------------------
// The destination resourceId must follow:
//   /subscriptions/.../resourceGroups/.../providers/Microsoft.Web/sites/{appName}/functions/{functionName}
// See Microsoft.EventGrid/topics/eventSubscriptions template reference.

var prepareFunctionId = '${func.id}/functions/PrepareSubmission'
var submitFunctionId = '${func.id}/functions/SubmitJob'
var pollFunctionId = '${func.id}/functions/PollExternalJob'
var completeFunctionId = '${func.id}/functions/CompleteRequest'

resource subPrepare 'Microsoft.EventGrid/topics/eventSubscriptions@2025-02-15' = {
  name: 'prepare-submission'
  parent: topic
  properties: {
    destination: {
      endpointType: 'AzureFunction'
      properties: {
        resourceId: prepareFunctionId
        maxEventsPerBatch: 1
        preferredBatchSizeInKilobytes: 64
      }
    }
    filter: {
      includedEventTypes: [
        'workflow.request.discovered.v1'
      ]
      isSubjectCaseSensitive: false
    }
    eventDeliverySchema: 'CloudEventSchemaV1_0'
    retryPolicy: {
      maxDeliveryAttempts: 10
      eventTimeToLiveInMinutes: 1440
    }
  }
}

resource subSubmit 'Microsoft.EventGrid/topics/eventSubscriptions@2025-02-15' = {
  name: 'submit-job'
  parent: topic
  properties: {
    destination: {
      endpointType: 'AzureFunction'
      properties: {
        resourceId: submitFunctionId
        maxEventsPerBatch: 1
        preferredBatchSizeInKilobytes: 64
      }
    }
    filter: {
      includedEventTypes: [
        'workflow.submission.prepared.v1'
      ]
      isSubjectCaseSensitive: false
    }
    eventDeliverySchema: 'CloudEventSchemaV1_0'
    retryPolicy: {
      maxDeliveryAttempts: 10
      eventTimeToLiveInMinutes: 1440
    }
  }
}

resource subPoll 'Microsoft.EventGrid/topics/eventSubscriptions@2025-02-15' = {
  name: 'poll-external-job'
  parent: topic
  properties: {
    destination: {
      endpointType: 'AzureFunction'
      properties: {
        resourceId: pollFunctionId
        maxEventsPerBatch: 1
        preferredBatchSizeInKilobytes: 64
      }
    }
    filter: {
      includedEventTypes: [
        'workflow.job.pollrequested.v1'
      ]
      isSubjectCaseSensitive: false
    }
    eventDeliverySchema: 'CloudEventSchemaV1_0'
    retryPolicy: {
      maxDeliveryAttempts: 10
      eventTimeToLiveInMinutes: 1440
    }
  }
}

resource subComplete 'Microsoft.EventGrid/topics/eventSubscriptions@2025-02-15' = {
  name: 'complete-request'
  parent: topic
  properties: {
    destination: {
      endpointType: 'AzureFunction'
      properties: {
        resourceId: completeFunctionId
        maxEventsPerBatch: 1
        preferredBatchSizeInKilobytes: 64
      }
    }
    filter: {
      includedEventTypes: [
        'workflow.job.terminal.v1'
      ]
      isSubjectCaseSensitive: false
    }
    eventDeliverySchema: 'CloudEventSchemaV1_0'
    retryPolicy: {
      maxDeliveryAttempts: 10
      eventTimeToLiveInMinutes: 1440
    }
  }
}

// ---------------------------
// Permissions (Managed Identity)
// ---------------------------

// Storage Table data access.
var roleStorageTableContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')

// Event Grid publish access.
var roleEventGridDataSender = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'd5a91429-5739-47e2-a06b-3470a27159e7')

resource raTable 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, func.identity.principalId, roleStorageTableContributor)
  scope: storage
  properties: {
    roleDefinitionId: roleStorageTableContributor
    principalId: func.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource raEventGridSend 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(topic.id, func.identity.principalId, roleEventGridDataSender)
  scope: topic
  properties: {
    roleDefinitionId: roleEventGridDataSender
    principalId: func.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Cosmos DB for NoSQL *data plane* role assignment (NOT Azure RBAC).
// Built-in Data Contributor role definition ID: 00000000-0000-0000-0000-000000000002
resource cosmosDataContributor 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-12-01-preview' = {
  parent: cosmos
  name: guid(cosmos.id, func.identity.principalId, 'cosmos-data-contributor')
  properties: {
    principalId: func.identity.principalId
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    scope: '/'
  }
}

// ---------------------------
// Outputs
// ---------------------------
output functionAppName string = func.name
output functionAppResourceId string = func.id
output storageAccountName string = storage.name
output tableName string = intakeTable.name
output cosmosAccountName string = cosmos.name
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output eventGridTopicName string = topic.name
output eventGridTopicEndpoint string = topic.properties.endpoint