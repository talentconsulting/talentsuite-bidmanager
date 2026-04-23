@description('Prefix for ACME bot resources (e.g. talentsuite-dev)')
param appNamePrefix string

@description('Email address for ACME/Let\'s Encrypt notifications')
param mailAddress string

@description('ACME directory endpoint URL')
param acmeEndpoint string = 'https://acme-v02.api.letsencrypt.org/'

@description('Create a new Key Vault alongside the ACME bot')
param createWithKeyVault bool = true

@description('Key Vault SKU name')
@allowed(['standard', 'premium'])
param keyVaultSkuName string = 'standard'

module acmebot 'br:cracmebotprod.azurecr.io/bicep/modules/keyvault-acmebot:v3' = {
  name: 'acmebot'
  params: {
    appNamePrefix: appNamePrefix
    mailAddress: mailAddress
    acmeEndpoint: acmeEndpoint
    createWithKeyVault: createWithKeyVault
    keyVaultSkuName: keyVaultSkuName
  }
}

output functionAppName string = acmebot.outputs.functionAppName
