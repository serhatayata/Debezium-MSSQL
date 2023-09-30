# Debezium-MSSQL

**INSERT**, **UPDATE**, and **DELETE** operations in the database involve the retrieval of changing and existing values. On the database side, these changes are recorded using **CDC** (Change Data Capture).

For **CDC** in MSSQL, it's worth noting the following:

- Prior to **SQL Server 2016 SP1**, it is supported in the Developer Edition and Enterprise Edition.
- After **SQL Server 2016 SP1**, it is also supported in the Standard Edition.

Additionally, we can track and record this data using triggers, but it will incur extra costs. CDC, on the other hand, can provide the service by reading the desired information from the database logs when activated.

As mentioned below, we will track changes made in the table under the specified schema. However, if desired, we can also track a specific column under this table.

Now, let's set up a sample project using Docker.

**docker-compose.yml**


```
version: '3.1'
services:
  zookeeper:
    image: wurstmeister/zookeeper:latest
    environment:
      ZOOKEEPER_CLIENT_PORT: 2181
      ZOOKEEPER_TICK_TIME: 2000
    ports:
      - 2181:2181
      - 2888:2888
      - 3888:3888

  kafka1:
    image: wurstmeister/kafka:latest
    restart: "no"
    ports:
      - 9092:9092
    environment:
      KAFKA_BROKER_ID: 1
      KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
      KAFKA_LISTENERS: INTERNAL://:29092,EXTERNAL://:9092
      KAFKA_ADVERTISED_LISTENERS: INTERNAL://kafka1:29092,EXTERNAL://localhost:9092
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: INTERNAL:PLAINTEXT,EXTERNAL:PLAINTEXT
      KAFKA_INTER_BROKER_LISTENER_NAME: INTERNAL
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1

  kafka2:
    image: wurstmeister/kafka:latest
    restart: "no"
    depends_on:
      - kafka1
    ports:
      - 9093:9093
    environment:
      KAFKA_BROKER_ID: 2
      KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
      KAFKA_LISTENERS: INTERNAL://:29093,EXTERNAL://:9093
      KAFKA_ADVERTISED_LISTENERS: INTERNAL://kafka2:29093,EXTERNAL://localhost:9093
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: INTERNAL:PLAINTEXT,EXTERNAL:PLAINTEXT
      KAFKA_INTER_BROKER_LISTENER_NAME: INTERNAL
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1

  kafdrop:
    image: obsidiandynamics/kafdrop
    restart: "no"
    environment:
      KAFKA_BROKERCONNECT: "kafka1:29092,kafka2:29093"
      JVM_OPTS: "-Xms16M -Xmx512M -Xss180K -XX:-TieredCompilation -XX:+UseStringDeduplication -noverify"
    ports:
      - 9000:9000
    depends_on:
      - kafka1
      - kafka2

  sqlserver:
    image: mcr.microsoft.com/mssql/server
    ports:
      - 1433:1433
    environment:
      - ACCEPT_EULA=Y
      - MSSQL_PID=Standard
      - SA_PASSWORD=sa.++112233
      - MSSQL_AGENT_ENABLED=true

  connect:
    image: debezium/connect
    ports:
      - 8083:8083
    environment:
      CONFIG_STORAGE_TOPIC: my_connect_configs
      OFFSET_STORAGE_TOPIC: my_connect_offsets
      STATUS_STORAGE_TOPIC: my_connect_statuses
      BOOTSTRAP_SERVERS: kafka1:29092,kafka2:29093
    depends_on:
      - kafka1
      - kafka2
      - zookeeper
      - sqlserver
```


YML file above;

- Kafka1
- Kafka2
- Zookeeper
- Kafdrop (UI Tool)
- SQLServer
- Debezium

are configured for these services.

Then run the compose file with the following command.

`docker-compose up -d`

After these operations, we can perform database side operations

```
// Creating database
CREATE DATABASE DebeziumTestDB

// Creating schema
CREATE SCHEMA debezium

// Creating table with schema
CREATE TABLE debezium.products
(
  id int primary key identity(1,1),
  name nvarchar(200),
  category nvarchar(max)
)

// Activating database CDC
EXEC sys.sp_cdc_enable_db

// Activating table CDC
USE DebeziumTestDB
EXEC sys.sp_cdc_enable_table 
@source_schema = N'debezium', 
@source_name = N'products', 
@role_name = NULL, 
@filegroup_name = N'', 
@supports_net_changes = 0
```


After these operations, we need to establish a connection between debezium and the database. For this, we create a connector by making a POST request to a Rest API endpoint on the debezium side.

**Endpoint**: <u>http://localhost:8083/connectors</u>

**Body** : 

```
{
  "name": "debezium-connector",
  "config": {
    "connector.class": "io.debezium.connector.sqlserver.SqlServerConnector",
    "topic.creation.enable": true,
    "topic.creation.default.replication.factor": 1,
    "topic.creation.default.partitions": 10,
    "topic.creation.default.cleanup.policy": "compact",
    "topic.creation.default.compression.type": "lz4",
    "auto.create.topics.enable": true,
    "database.server.name": "dbserver1",
    "database.hostname": "sqlserver",
    "database.port": "1433",
    "database.user": "sa",
    "database.password": "sa.++112233",
    "database.names": "DebeziumTestDB",
    "database.whitelist": "debezium.products",
    "database.history.kafka.topic": "dbhistory",
    "database.encrypt": "false",
    "database.history.kafka.bootstrap.servers": "kafka1:29092,kafka2:29093",
    "table.whitelist": "products",
    "schema.include.list": "debezium",
    "schema.history.internal.kafka.topic": "schemahistory",
    "schema.history.internal.kafka.bootstrap.servers": "kafka1:29092,kafka2:29093",
    "table.include.list": "debezium.products",
    "topic.prefix": "topicprefix"
  }
}
```

After all these operations, let's create a Console application and access CDC data via Kafka.

![CDC Akışı](https://dev-to-uploads.s3.amazonaws.com/uploads/articles/ron99bgup5xaa1k4vvso.png)

```
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
        Console.WriteLine($"Value : {value.Value}");
    }
}
catch (System.Exception ex)
{
    Console.WriteLine(ex.Message);
}
```

If we run the application and the following query is run on the database side, we will get an answer like this.

```
INSERT INTO debezium.products
VALUES('product 6','test category 6')
```

```
{
  "schema": {
    "type": "struct",
    "fields": [
      {
        "type": "struct",
        "fields": [
          {
            "type": "int32",
            "optional": false,
            "field": "id"
          },
          {
            "type": "string",
            "optional": true,
            "field": "name"
          },
          {
            "type": "string",
            "optional": true,
            "field": "category"
          }
        ],
        "optional": true,
        "name": "topicprefix.DebeziumTestDB.debezium.products.Value",
        "field": "before"
      },
      {
        "type": "struct",
        "fields": [
          {
            "type": "int32",
            "optional": false,
            "field": "id"
          },
          {
            "type": "string",
            "optional": true,
            "field": "name"
          },
          {
            "type": "string",
            "optional": true,
            "field": "category"
          }
        ],
        "optional": true,
        "name": "topicprefix.DebeziumTestDB.debezium.products.Value",
        "field": "after"
      },
      {
        "type": "struct",
        "fields": [
          {
            "type": "string",
            "optional": false,
            "field": "version"
          },
          {
            "type": "string",
            "optional": false,
            "field": "connector"
          },
          {
            "type": "string",
            "optional": false,
            "field": "name"
          },
          {
            "type": "int64",
            "optional": false,
            "field": "ts_ms"
          },
          {
            "type": "string",
            "optional": true,
            "name": "io.debezium.data.Enum",
            "version": 1,
            "parameters": {
              "allowed": "true,last,false,incremental"
            },
            "default": "false",
            "field": "snapshot"
          },
          {
            "type": "string",
            "optional": false,
            "field": "db"
          },
          {
            "type": "string",
            "optional": true,
            "field": "sequence"
          },
          {
            "type": "string",
            "optional": false,
            "field": "schema"
          },
          {
            "type": "string",
            "optional": false,
            "field": "table"
          },
          {
            "type": "string",
            "optional": true,
            "field": "change_lsn"
          },
          {
            "type": "string",
            "optional": true,
            "field": "commit_lsn"
          },
          {
            "type": "int64",
            "optional": true,
            "field": "event_serial_no"
          }
        ],
        "optional": false,
        "name": "io.debezium.connector.sqlserver.Source",
        "field": "source"
      },
      {
        "type": "string",
        "optional": false,
        "field": "op"
      },
      {
        "type": "int64",
        "optional": true,
        "field": "ts_ms"
      },
      {
        "type": "struct",
        "fields": [
          {
            "type": "string",
            "optional": false,
            "field": "id"
          },
          {
            "type": "int64",
            "optional": false,
            "field": "total_order"
          },
          {
            "type": "int64",
            "optional": false,
            "field": "data_collection_order"
          }
        ],
        "optional": true,
        "name": "event.block",
        "version": 1,
        "field": "transaction"
      }
    ],
    "optional": false,
    "name": "topicprefix.DebeziumTestDB.debezium.products.Envelope",
    "version": 1
  },
  "payload": {
    "before": null,
    "after": {
      "id": 4,
      "name": "product 6",
      "category": "test category 6"
    },
    "source": {
      "version": "2.2.0.Alpha3",
      "connector": "sqlserver",
      "name": "topicprefix",
      "ts_ms": 1696104363803,
      "snapshot": "false",
      "db": "DebeziumTestDB",
      "sequence": null,
      "schema": "debezium",
      "table": "products",
      "change_lsn": "0000002a:000003a8:0002",
      "commit_lsn": "0000002a:000003a8:0003",
      "event_serial_no": 1
    },
    "op": "c",
    "ts_ms": 1696104366879,
    "transaction": null
  }
}
```

