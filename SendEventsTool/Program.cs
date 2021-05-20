using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Amazon.KinesisFirehose;
using Amazon.SQS;
using Amazon.SQS.Model;

// initialize global state
Random random = new();
AmazonKinesisClient _kinesisClient = new();
AmazonKinesisFirehoseClient _firehoseClient = new();
AmazonSQSClient _sqsClient = new();

// check arguments
try {
    Console.WriteLine("SendEventsTool");

    // check command line arguments
    switch(args.Length) {
    case 1:
        if(args[0].Contains(":kinesis:", StringComparison.Ordinal)) {
            var records = CreateRecords();
            await WriteKinesisStreamRecords(_kinesisClient, args[0], records);
            Console.WriteLine($"SUCCESS: sent {records.Count():N0} Kinesis Stream records");
        } else if(args[0].Contains(":firehose:", StringComparison.Ordinal)) {
            var records = CreateRecords();
            await WriteFirehoseRecords(_firehoseClient, args[0], records);
            Console.WriteLine($"SUCCESS: sent {records.Count():N0} Kinesis Stream records");
        } else if(args[0].Contains(":sqs:", StringComparison.Ordinal)) {
            var records = CreateRecords();
            await SendSqsRecords(_sqsClient, args[0], records);
            Console.WriteLine($"SUCCESS: sent {records.Count():N0} SQS records");
        } else {
            Console.WriteLine("Argument must either be a Kinesis Stream, Firehose, or SQS");
            return;
        }
        break;
    case 0:
        Console.WriteLine("Missing argument");
        break;
    default:
        Console.WriteLine("Only one argument expected (Kinesis Stream, Firehose, or SQS)");
        break;
    }
} catch(NotImplementedException e) {
    Console.WriteLine($"ERROR: {e.Message}");
}

// local functions
IEnumerable<(string Id, string Body)> CreateRecords() {
    List<(string Id, string Body)> result = new();
    for(var i = 0; i < 100 + random.Next(100); ++i) {
        var guid = Guid.NewGuid().ToString();
        var evenType = random.Next(3) switch {
            0 => "Create",
            1 => "Update",
            2 => "Delete",
            var x => throw new InvalidOperationException($"unexpected random number: {x}")
        };
        result.Add(new() {
            Id = guid,
            Body = JsonSerializer.Serialize(new {
                Id = guid,
                EventType = evenType,
                Message = $"Message {guid}#{i:N0} - Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum."
            })
        });
    }
    return result;
}

static Task WriteKinesisStreamRecords(IAmazonKinesis kinesisClient, string arn, IEnumerable<(string Id, string Body)> records)
    => kinesisClient.PutRecordsAsync(new() {
        StreamName = arn.Split('/', 2)[1],
        Records = records.Select(record => new PutRecordsRequestEntry {
            PartitionKey = record.Id,
            Data = new MemoryStream(Encoding.UTF8.GetBytes(record.Body))
        }).ToList()
    });

static Task WriteFirehoseRecords(IAmazonKinesisFirehose firehoseClient, string arn, IEnumerable<(string Id, string Body)> records)
    => firehoseClient.PutRecordBatchAsync(new() {
        DeliveryStreamName = arn.Split('/', 2)[1],
        Records = records.Select(record => new Amazon.KinesisFirehose.Model.Record {
            Data = new MemoryStream(Encoding.UTF8.GetBytes(record.Body + "\n"))
        }).ToList()
    });

static async Task SendSqsRecords(IAmazonSQS sqsClient, string arn, IEnumerable<(string Id, string Body)> records) {

    // convert from 'arn:aws:sqs:us-east-2:123456789012:aa4-MyQueue-Z5NOSZO2PZE9'
    //  to 'https://sqs.us-east-2.amazonaws.com/123456789012/aa4-MyQueue-Z5NOSZO2PZE9'
    var parts = arn.Split(':');
    if((parts.Length != 6) || (parts[0] != "arn") || (parts[1] != "aws") || (parts[2] != "sqs")) {
        throw new ArgumentException("unexpected format", nameof(arn));
    }
    var region = parts[3];
    var accountId = parts[4];
    var queueName = parts[5];
    var queueUrl = $"https://sqs.{region}.amazonaws.com/{accountId}/{queueName}";

    // send message
    while(records.Any()) {

        // can only send 10 records at a time
        var batch = records.Take(10);
        records = records.Skip(10);

        // sent batch
        await sqsClient.SendMessageBatchAsync(new() {
            QueueUrl = queueUrl,
            Entries = batch.Select(record => new SendMessageBatchRequestEntry {
                Id = record.Id,
                MessageBody = record.Body
            }).ToList()
        });
    }
}