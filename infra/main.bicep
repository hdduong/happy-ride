param location string = 'southeastasia'
param postgresAdminLogin string = 'happyride'
@secure()
param postgresAdminPassword string
param apiAppName string = 'app-grabbike-api'
var nameSuffix = uniqueString(resourceGroup().id)

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sb-grabbike-${nameSuffix}'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource dispatchQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: 'dispatch'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT1M'
  }
}

resource dispatchAccess 'Microsoft.ServiceBus/namespaces/queues/authorizationRules@2022-10-01-preview' = {
  parent: dispatchQueue
  name: 'dispatch-worker'
  properties: {
    rights: [
      'Listen'
      'Send'
    ]
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'psql-grabbike-${nameSuffix}'
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    authConfig: {
      passwordAuth: 'Enabled'
      activeDirectoryAuth: 'Disabled'
    }
  }
}

resource postgis 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: {
    value: 'POSTGIS'
    source: 'user-override'
  }
}

resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'happyride'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

output postgresHost string = postgres.properties.fullyQualifiedDomainName
output serviceBusNamespaceName string = serviceBus.name
output dispatchQueueName string = dispatchQueue.name
output apiAppName string = apiAppName
