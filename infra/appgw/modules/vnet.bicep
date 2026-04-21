var vnetName = 'vnet-talentsuite-dev'
var talentgatewaySubnetName = 'talent-appgateway-subnet'

resource vnet 'Microsoft.Network/virtualNetworks@2023-04-01' existing = {
  name: vnetName

  resource subnet 'subnets' = {
    name: talentgatewaySubnetName
    properties: {
      addressPrefix: '10.42.3.0/24'
      privateEndpointNetworkPolicies: 'Disabled'
    }
  }
}

output vnetId string = vnet.id
output appGwSubnetId string = vnet::subnet.id
