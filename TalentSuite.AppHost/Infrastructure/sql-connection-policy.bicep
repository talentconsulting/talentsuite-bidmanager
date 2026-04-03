param sqlServerName string

resource sqlConnectionPolicy 'Microsoft.Sql/servers/connectionPolicies@2023-08-01' = {
  name: '${sqlServerName}/default'
  location: resourceGroup().location
  properties: {
    connectionType: 'Proxy'
  }
}
