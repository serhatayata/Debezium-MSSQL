using Confluent.Kafka;

try
{
    var configuration = new ConsumerConfig() 
    { 
        GroupId = "topicprefix.DebeziumTestDB.debezium.products",
        BootstrapServers = "localhost:9092"
    };

    using var consumer = new ConsumerBuilder<string, string>(configuration).Build();
    consumer.Subscribe("topicprefix.DebeziumTestDB.debezium.products");

    while (true)
    {
        ConsumeResult<string, string> value = consumer.Consume();
        Console.WriteLine($"Topic : {value.TopicPartitionOffset}");
        Console.WriteLine($"Message : {value.Message.Value}");
        Console.WriteLine($"Value : {value.Value}");
    }
}
catch (System.Exception ex)
{
    Console.WriteLine(ex.Message);
}