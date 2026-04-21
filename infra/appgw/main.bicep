@description('Environment Name')
param envName string

@description('Azure Region')
param location string = resourceGroup().location

@description('Frontend backend host name (Storage Account Web Endpoint)')
param frontendBackendFqdn string

@description('API Container App FQDN')
param apiBackendFqdn string

@description('Keycloak Container App FQDN')
param keycloakBackendFqdn string

@description('Grafana Container App FQDN')
param grafanaBackendFqdn string

@description('Frontend Custom Domain (e.g. dev.talentsuite.uk)')
param frontendCustomDomain string

@description('API Custom Domain (e.g. dev-api.talentsuite.uk)')
param apiCustomDomain string

@description('Auth Custom Domain (e.g. auth-dev.talentsuite.uk)')
param authCustomDomain string

@description('Grafana Custom Domain (e.g. grafana-dev.talentsuite.uk)')
param grafanaCustomDomain string

var appGwName = 'appgw-${envName}'
//var vnetName = 'vnet-${envName}'
var publicIpName = 'pip-appgw-${envName}'

module vnet 'modules/vnet.bicep' = {
  name: 'deploy-vnet'
}

module publicIp 'modules/publicIp.bicep' = {
  name: 'deploy-publicIp'
  params: {
    publicIpName: publicIpName
    location: location
  }
}

module appGw 'modules/appgw.bicep' = {
  name: 'deploy-appgw'
  params: {
    appGwName: appGwName
    location: location
    subnetId: vnet.outputs.appGwSubnetId
    publicIpId: publicIp.outputs.publicIpId
    frontendBackendFqdn: frontendBackendFqdn
    apiBackendFqdn: apiBackendFqdn
    keycloakBackendFqdn: keycloakBackendFqdn
    grafanaBackendFqdn: grafanaBackendFqdn
    frontendCustomDomain: frontendCustomDomain
    apiCustomDomain: apiCustomDomain
    authCustomDomain: authCustomDomain
    grafanaCustomDomain: grafanaCustomDomain
  }
}

output appGwPublicIp string = publicIp.outputs.publicIpAddress
