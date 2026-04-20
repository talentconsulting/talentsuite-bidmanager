var vnetName = 'vnet-talentsuite-dev'
var talentgatewaySubnetName = 'talent-appgateway-subnet'

resource vnet 'Microsoft.Network/virtualNetworks@2023-04-01' existing = {
  name: vnetName
  
  resource subnet 'Microsoft.Network/virtualNetworks/subnets@2023-04-01' existing = {
    name: talentgatewaySubnetName
  }
}

output vnetId string = vnet.id
output appGwSubnetId string = vnet.properties.subnets[0].id
