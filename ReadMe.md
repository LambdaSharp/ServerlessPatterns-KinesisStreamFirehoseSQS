# Kinesis Stream vs. Kinesis Firehose vs. SQS

This repository shows how to implement consumers for Kinesis Stream, Firehose, and SQS.

## Kinesis Stream

Run `lash deploy KinesisStream` to deploy the CloudFormation stack. The stack creates a Kinesis Stream and two Lambda functions consuming for it. The first function (`StoreFunction`) combines records and stores them into S3 using GZIP compression. The second function (`EmitFunction`) emits each received event on a matching SNS topic.

Run `dotnet run -p SendEventsTool/SendEventsTool.csproj -- <ARN>` where `<ARN>` is the Kinesis Stream ARN emitted by `lash` after the CloudFormation stack was created.

**Notes:**

* Kinesis Stream require provisioned capacity by allocating shards. Each shard provides ingestion up to 1 MiB/second and 1000 records/second. A shard can emit up to 2 MiB/second. Shards are not load balanced and it is the responsibility of the event sender to distribute events as equally as possible.

* By default, the Kinesis Stream is configured to only retain records for up to 24 hours. After that time, records will be lost. The record retention period can be configured using the `RetentionPeriodHours` property. It is recommended to retain records long enough that any processing disruption can be addressed before they expire.

* The reading semantics of Kinesis Stream are similar to that of a file stream. For instance, it is not possible to read the next batch of records until the current batch has been processed successfully. It is therefore extremely important to have good failure handling so that each batch of records can be successfully processed.

* The Lambda functions are invoked with a 100ms lag as soon as event data is received. The attached Lambda functions are invoked concurrently for each provisioned shard.

## Kinesis Firehose

Run `lash deploy KinesisFirehose` to deploy the CloudFormation stack. The stack creates a Kinesis Firehose and a Lambda function to inspect the events. The Lambda function (`EmitFunction`) emits each inspected event on a matching SNS topic. The Kinesis Firehose automatically stores any received events into the configured S3 bucket.

Run `dotnet run -p SendEventsTool/SendEventsTool.csproj -- <ARN>` where `<ARN>` is the Kinesis Firehose ARN emitted by `lash` after the CloudFormation stack was created.

**Notes:**

* Kinesis Firehose automatically adapts to the incoming volume of events. There is no need to provision it for expected capacity.

* Kinesis Firehose is designed to accumulate as much data as possible before processing it. Consequently, it has much higher latency than a Kinesis Stream. The minimum accumulation period that can be configured for a Lambda transformer is either 60 seconds or 1MB of data, whichever happens first.

* Kinesis Firehose handles the storing of compressed records in an S3 bucket. However, the records are simply concatenated. It is therefore necessary to add a separator character at the origin where the events are emitted to ensure they can be properly parsed once combined.

## SQS

Run `lash deploy SQS` to deploy the CloudFormation stack. The stack creates an SQS queue and a Lambda function to receive the events. The Lambda function (`StoreAndEmitFunction`) stores and emits each received event.

Run `dotnet run -p SendEventsTool/SendEventsTool.csproj -- <ARN>` where `<ARN>` is the SQS ARN emitted by `lash` after the CloudFormation stack was created.

**Notes:**

* The Lambda function can at most read 10 events in a batch from the SQS queue. The `ALambdaQueueFunction` base class limits this further to 1 event as it is designed to handle SQS records independently from each other.

* Unlike Kinesis Stream/Firehose, the Lambda function can skip over failed records to receive subsequent records. This means records can be processed out of order.

* By default, the SQS queue is configured to retain records for up to 4 days. After that time, records will be lost. The record retention period can be configured using the `MessageRetentionPeriod` property. It is recommended to retain records long enough that any processing disruption can be addressed before they expire.
