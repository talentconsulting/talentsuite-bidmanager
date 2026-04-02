param location string = resourceGroup().location
param sqlServerName string

var vnetName = 'vnet-talentsuite-dev'
var acaSubnetName = 'aca-infrastructure'
var privateEndpointSubnetName = 'private-endpoints'
var privateDnsZoneName = 'privatelink.database.windows.net'

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.42.0.0/16'
      ]
    }
  }
}

resource acaSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = {
  parent: vnet
  name: acaSubnetName
  properties: {
    addressPrefix: '10.42.0.0/23'
    delegations: [
      {
        name: 'aca-delegation'
        properties: {
          serviceName: 'Microsoft.App/environments'
        }
      }
    ]
  }
}

resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = {
  parent: vnet
  name: privateEndpointSubnetName
  properties: {
    addressPrefix: '10.42.2.0/24'
    privateEndpointNetworkPolicies: 'Disabled'
  }
}

resource sqlPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: privateDnsZoneName
  location: 'global'
}

resource sqlPrivateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: sqlPrivateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}

resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-07-01' = {
  name: 'pep-sql-talentsuite-dev'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnet.id
    }
    privateLinkServiceConnections: [
      {
        name: 'sqlServerConnection'
        properties: {
          privateLinkServiceId: resourceId('Microsoft.Sql/servers', sqlServerName)
          groupIds: [
            'sqlServer'
          ]
        }
      }
    ]
  }
}

resource sqlPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-07-01' = {
  parent: sqlPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'sqlServerDnsZone'
        properties: {
          privateDnsZoneId: sqlPrivateDnsZone.id
        }
      }
    ]
  }
}

output acaInfrastructureSubnetId string = acaSubnet.id
