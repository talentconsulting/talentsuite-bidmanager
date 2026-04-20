param appGwName string
param location string
param subnetId string
param publicIpId string

param frontendBackendFqdn string
param apiBackendFqdn string
param keycloakBackendFqdn string
param grafanaBackendFqdn string

param frontendCustomDomain string
param apiCustomDomain string
param authCustomDomain string
param grafanaCustomDomain string

resource appGw 'Microsoft.Network/applicationGateways@2023-04-01' = {
  name: appGwName
  location: location
  properties: {
    sku: {
      name: 'WAF_v2'
      tier: 'WAF_v2'
      capacity: 2
    }
    gatewayIPConfigurations: [
      {
        name: 'appGatewayIpConfig'
        properties: {
          subnet: {
            id: subnetId
          }
        }
      }
    ]
    frontendIPConfigurations: [
      {
        name: 'appGwPublicFrontendIp'
        properties: {
          publicIPAddress: {
            id: publicIpId
          }
        }
      }
    ]
    frontendPorts: [
      {
        name: 'port_80'
        properties: {
          port: 80
        }
      }
    ]
    backendAddressPools: [
      {
        name: 'frontendBackendPool'
        properties: {
          backendAddresses: [ { fqdn: frontendBackendFqdn } ]
        }
      }
      {
        name: 'apiBackendPool'
        properties: {
          backendAddresses: [ { fqdn: apiBackendFqdn } ]
        }
      }
      {
        name: 'authBackendPool'
        properties: {
          backendAddresses: [ { fqdn: keycloakBackendFqdn } ]
        }
      }
      {
        name: 'grafanaBackendPool'
        properties: {
          backendAddresses: [ { fqdn: grafanaBackendFqdn } ]
        }
      }
    ]
    probes: [
      {
        name: 'frontendProbe'
        properties: {
          protocol: 'Http'
          path: '/'
          interval: 30
          timeout: 30
          unhealthyThreshold: 3
          pickHostNameFromBackendHttpSettings: true
        }
      }
      {
        name: 'apiProbe'
        properties: {
          protocol: 'Https'
          path: '/api/health'
          interval: 30
          timeout: 30
          unhealthyThreshold: 3
          pickHostNameFromBackendHttpSettings: true
        }
      }
      {
        name: 'authProbe'
        properties: {
          protocol: 'Https'
          path: '/realms/master/.well-known/openid-configuration'
          interval: 30
          timeout: 30
          unhealthyThreshold: 3
          pickHostNameFromBackendHttpSettings: true
        }
      }
      {
        name: 'grafanaProbe'
        properties: {
          protocol: 'Https'
          path: '/api/health'
          interval: 30
          timeout: 30
          unhealthyThreshold: 3
          pickHostNameFromBackendHttpSettings: true
        }
      }
    ]
    backendHttpSettingsCollection: [
      {
        name: 'frontendHttpSettings'
        properties: {
          port: 80
          protocol: 'Http'
          cookieBasedAffinity: 'Disabled'
          pickHostNameFromBackendAddress: true
          requestTimeout: 20
          probe: { id: resourceId('Microsoft.Network/applicationGateways/probes', appGwName, 'frontendProbe') }
        }
      }
      {
        name: 'apiHttpSettings'
        properties: {
          port: 443
          protocol: 'Https'
          cookieBasedAffinity: 'Disabled'
          pickHostNameFromBackendAddress: true
          requestTimeout: 20
          probe: { id: resourceId('Microsoft.Network/applicationGateways/probes', appGwName, 'apiProbe') }
        }
      }
      {
        name: 'authHttpSettings'
        properties: {
          port: 443
          protocol: 'Https'
          cookieBasedAffinity: 'Disabled'
          pickHostNameFromBackendAddress: true
          requestTimeout: 20
          probe: { id: resourceId('Microsoft.Network/applicationGateways/probes', appGwName, 'authProbe') }
        }
      }
      {
        name: 'grafanaHttpSettings'
        properties: {
          port: 443
          protocol: 'Https'
          cookieBasedAffinity: 'Disabled'
          pickHostNameFromBackendAddress: true
          requestTimeout: 20
          probe: { id: resourceId('Microsoft.Network/applicationGateways/probes', appGwName, 'grafanaProbe') }
        }
      }
    ]
    httpListeners: [
      {
        name: 'frontendListener'
        properties: {
          frontendIPConfiguration: { id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGwName, 'appGwPublicFrontendIp') }
          frontendPort: { id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGwName, 'port_80') }
          protocol: 'Http'
          hostName: frontendCustomDomain
          requireServerNameIndication: false
        }
      }
      {
        name: 'apiListener'
        properties: {
          frontendIPConfiguration: { id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGwName, 'appGwPublicFrontendIp') }
          frontendPort: { id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGwName, 'port_80') }
          protocol: 'Http'
          hostName: apiCustomDomain
          requireServerNameIndication: false
        }
      }
      {
        name: 'authListener'
        properties: {
          frontendIPConfiguration: { id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGwName, 'appGwPublicFrontendIp') }
          frontendPort: { id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGwName, 'port_80') }
          protocol: 'Http'
          hostName: authCustomDomain
          requireServerNameIndication: false
        }
      }
      {
        name: 'grafanaListener'
        properties: {
          frontendIPConfiguration: { id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGwName, 'appGwPublicFrontendIp') }
          frontendPort: { id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGwName, 'port_80') }
          protocol: 'Http'
          hostName: grafanaCustomDomain
          requireServerNameIndication: false
        }
      }
    ]
    requestRoutingRules: [
      {
        name: 'frontendRoutingRule'
        properties: {
          ruleType: 'Basic'
          priority: 100
          httpListener: { id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGwName, 'frontendListener') }
          backendAddressPool: { id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', appGwName, 'frontendBackendPool') }
          backendHttpSettings: { id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', appGwName, 'frontendHttpSettings') }
        }
      }
      {
        name: 'apiRoutingRule'
        properties: {
          ruleType: 'Basic'
          priority: 110
          httpListener: { id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGwName, 'apiListener') }
          backendAddressPool: { id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', appGwName, 'apiBackendPool') }
          backendHttpSettings: { id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', appGwName, 'apiHttpSettings') }
        }
      }
      {
        name: 'authRoutingRule'
        properties: {
          ruleType: 'Basic'
          priority: 120
          httpListener: { id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGwName, 'authListener') }
          backendAddressPool: { id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', appGwName, 'authBackendPool') }
          backendHttpSettings: { id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', appGwName, 'authHttpSettings') }
        }
      }
      {
        name: 'grafanaRoutingRule'
        properties: {
          ruleType: 'Basic'
          priority: 130
          httpListener: { id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGwName, 'grafanaListener') }
          backendAddressPool: { id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', appGwName, 'grafanaBackendPool') }
          backendHttpSettings: { id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', appGwName, 'grafanaHttpSettings') }
        }
      }
    ]
  }
}
