using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SimpleNotificationService;
using LambdaSharp;
using LambdaSharp.SimpleQueueService;

namespace ServerlessPatterns.SQS.StoreAndEmitFunction {

    public class Event {

        //--- Properties ---
        public string Id { get; set; }
        public string EventType { get; set; }
        public string Message { get; set; }
    }

    public sealed class Function : ALambdaQueueFunction<Event> {

        //--- Fields ---
        private string _bucketName;
        private IAmazonS3 _s3Client;
        private string _contentCreatedTopicArn;
        private string _contentUpdatedTopicArn;
        private string _contentDeletedTopicArn;
        private IAmazonSimpleNotificationService _snsClient;

        //--- Constructors ---
        public Function() : base(new LambdaSharp.Serialization.LambdaSystemTextJsonSerializer()) { }

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // read configuration
            _bucketName = config.ReadS3BucketName("BucketArn");
            _contentCreatedTopicArn = config.ReadText("ContentCreatedTopicArn");
            _contentUpdatedTopicArn = config.ReadText("ContentUpdatedTopicArn");
            _contentDeletedTopicArn = config.ReadText("ContentDeletedTopicArn");

            // initialize clients
            _s3Client = new AmazonS3Client();
            _snsClient = new AmazonSimpleNotificationServiceClient();
        }

        public override async Task ProcessMessageAsync(Event evt) {
            LogInfo($"Received event {CurrentRecord.MessageId}");

            // store and emit all records
            var originalSize = CurrentRecord.Body.Length;

            // compress event data for storage
            using var eventStream = new MemoryStream();
            using(var gzipStream = new GZipStream(eventStream, CompressionLevel.Optimal, leaveOpen: true)) {
                var buffer = Encoding.UTF8.GetBytes(CurrentRecord.Body);
                gzipStream.Write(buffer, 0, buffer.Length);
            }
            eventStream.Position = 0;
            var compressedSize = eventStream.Length;

            // store event in S3
            var s3Key = $"{CurrentRecord.MessageId}.gz";
            await _s3Client.PutObjectAsync(new PutObjectRequest {
                BucketName = _bucketName,
                Key = s3Key,
                InputStream = eventStream
            });
            LogInfo($"Stored {s3Key} (original: {originalSize} bytes, stored: {compressedSize:N0} bytes)");

            // determine best SNS topic based on event type
            var topicArn = evt.EventType switch {
                "Create" => _contentCreatedTopicArn,
                "Update" => _contentUpdatedTopicArn,
                "Delete" => _contentDeletedTopicArn,

                // TODO: better alternative is to send unrecognized events to a Dead-Letter queue
                var eventType => throw new InvalidOperationException($"Unrecognized event type: '{eventType}'")
            };

            // emit event
            await _snsClient.PublishAsync(topicArn, CurrentRecord.Body);
        }
    }
}
