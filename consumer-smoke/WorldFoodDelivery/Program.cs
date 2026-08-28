using WorldFoodDelivery.Application;
using WorldFoodDelivery.Infrastructure;
using WorldFoodDelivery.Persistence;
using WorldFoodDelivery.Relational;
using WorldFoodDelivery.Verification;

var csvPath = args.ElementAtOrDefault(0) ?? "orders.csv";
var outputDirectory = args.ElementAtOrDefault(1) ?? "artifacts";
var scenario = new FoodDeliveryScenario(
    new FoodDeliveryContext(new ConsumerSmokeProvider()),
    new FoodOrderCsvReader(),
    new FoodOrderImporter(),
    new GraphScenarioVerifier(),
    new RelationalInspectionWorkflow(new WorldFoodRelationalInspectionHost()),
    new RelationalScenarioVerifier());
var result = await scenario.RunAsync(csvPath, outputDirectory);

Console.WriteLine(
    $"Clean-room consumer smoke passed: {result.CsvRows} CSV rows, " +
    $"{result.AffectedNodes} nodes, {result.AffectedRelations} relations, " +
    $"{result.RelationalObjects} relational objects, {result.RelationalRelations} foreign-key relations. " +
    $"Artifacts: {Path.GetFullPath(outputDirectory)}");
