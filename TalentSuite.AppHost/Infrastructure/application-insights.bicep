param location string = resourceGroup().location

var appInsightsName = 'appi-${take(uniqueString(resourceGroup().id), 8)}'

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Bluefield'
    IngestionMode: 'ApplicationInsights'
  }
}

output applicationInsightsConnectionString string = appInsights.properties.ConnectionString
