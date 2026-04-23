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

@description('Key Vault Name (optional)')
param keyVaultName string = ''

@description('Key Vault Secret Name for the Certificate (optional)')
param certificateSecretName string = ''

var appGwName = 'appgw-${envName}'
var publicIpName = 'pip-appgw-${envName}'
var appGwIdentityName = 'id-appgw-${envName}'

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' existing = if (!empty(keyVaultName)) {
  name: keyVaultName
}

resource appGwIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = if (!empty(keyVaultName)) {
  name: appGwIdentityName
  location: location
}

// "Key Vault Secrets User" Role Definition ID
var secretsUserRoleId = resourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource keyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(keyVaultName)) {
  scope: keyVault
  name: guid(keyVault.id, appGwIdentityName, secretsUserRoleId)
  properties: {
    roleDefinitionId: secretsUserRoleId
    principalId: appGwIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

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
    appGwIdentityId: !empty(keyVaultName) ? appGwIdentity.id : ''
    keyVaultSecretUri: (!empty(keyVaultName) && !empty(certificateSecretName)) ? '${keyVault.properties.vaultUri}secrets/${certificateSecretName}' : ''
  }
}

output appGwPublicIp string = publicIp.outputs.publicIpAddress
