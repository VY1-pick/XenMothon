using XenMotion.Server;

var app = await CatalogServer.CreateAsync(args);
await app.RunAsync();
