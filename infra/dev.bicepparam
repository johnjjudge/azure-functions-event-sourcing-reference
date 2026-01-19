using './main.bicep'

// Example parameters for a dev deployment.
//
// Usage:
//   az deployment group create \
//     --resource-group <rg> \
//     --template-file infra/main.bicep \
//     --parameters infra/dev.bicepparam

param namePrefix = 'wsdemo'

// The external service is deployed separately.
param externalServiceBaseUrl = 'https://example-external-service.contoso.net'
param externalServiceAudience = 'api://externalservice'

// Optional overrides
param intakeTableName = 'Intake'
param discoverSchedule = '0 */5 * * * *'
param pollSchedule = '0 */5 * * * *'
param enableAppInsights = true
