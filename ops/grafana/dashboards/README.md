# Grafana Dashboard Imports

These JSON files are ready to import into the `grafana` instance in this repo.

Import path in Grafana:

1. Open `Dashboards`
2. Select `New`
3. Select `Import`
4. Upload one of the JSON files in this folder
5. Choose the `Azure Monitor` datasource

Recommended starting set for this stack:

- `azure-container-apps-aggregate-view.json`
  - fleet-level view across Container Apps
- `azure-container-apps-container-app-view.json`
  - deeper per-app view for `talentserver`, `talentfunctions`, `keycloak`, and `grafana`
- `azure-insights-networks-front-door.json`
  - Front Door traffic, latency, health, and edge behaviour
- `azure-service-bus.json`
  - queue and namespace monitoring for messaging used by Functions
- `azure-storage-accounts.json`
  - storage health, transactions, latency, and availability
- `azure-insights-applications-overview.json`
  - Application Insights overview if the app resources are sending telemetry there

Notes:

- These are public Grafana dashboard imports downloaded from Grafana's dashboard gallery and intended for the built-in `Azure Monitor` datasource.
- Some dashboards expect you to select the subscription, resource group, or resource after import.
- The Front Door dashboard file is generic for Azure networking resources; use it for the Front Door profile and related traffic investigation.
