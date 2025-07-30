namespace Core.CrossCuttingConcerns.Logging.Serilog.ConfigurationModels;

public class MongoDbConfiguration
{
    public string ConnectionString { get; set; }
    public string Database { get; set; }
    public string Collection { get; set; }

    public MongoDbConfiguration()
    {
        ConnectionString = string.Empty;
        Collection = string.Empty;
        Database = string.Empty;
    }

    public MongoDbConfiguration(string connectionString,string database ,string collection)
    {
        ConnectionString = connectionString;
        Database = database;
        Collection = collection;
    }
}
