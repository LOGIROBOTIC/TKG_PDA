# TKG RDS Script

`tkg_integration.js` is installed only through the supported RDS **Script** interface. It does not modify RDS internal files or databases.

Paste the version-controlled artifact into the supported RDS Script editor and install it from `boot()` using secrets that are not committed:

```js
var TKG_RDS_INTEGRATION_CONFIG = {
  integrationKey: "<environment secret>",
  callbackUrl: "http://127.0.0.1:<TKG_PORT>/api/integrations/rds/events",
  callbackKey: "<environment secret>"
};

function boot() {
  TkgRdsIntegration.install(jj, TKG_RDS_INTEGRATION_CONFIG);
}
```

Do not edit RDS internal files or databases. Save/reload through the Script UI, then validate the non-mutating endpoint before enabling backend dispatch. A real Wind Task label and its exact input variables must already exist; no tutorial label is substituted.

Run the local contract harness with:

```text
node --test rds/scripts/test/tkg_integration.test.js
```
