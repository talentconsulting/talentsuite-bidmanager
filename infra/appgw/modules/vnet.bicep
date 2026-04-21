var vnetName = 'vnet-talentsuite-dev'
var talentgatewaySubnetName = 'talent-appgateway-subnet'

resource vnet 'Microsoft.Network/virtualNetworks@2023-04-01' existing = {
  name: vnetName

  resource subnet 'subnets' existing = {
    name: talentgatewaySubnetName
  }
}

output vnetId string = vnet.id
output appGwSubnetId string = vnet::subnet.id
