param publicIpName string
param location string

resource publicIp 'Microsoft.Network/publicIPAddresses@2023-04-01' = {
  name: publicIpName
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
  }
}

output publicIpId string = publicIp.id
output publicIpAddress string = publicIp.properties.ipAddress
