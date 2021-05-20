using System;
using System.Threading.Tasks;
using LambdaSharp;
using Amazon.Lambda.KinesisFirehoseEvents;
using Amazon.SimpleNotificationService;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace ServerlessPatterns.KinesisFirehose.EmitFunction {

    public sealed class Function : ALambdaFunction<KinesisFirehoseEvent, KinesisFirehoseResponse> {

        //--- Types ---
        private class EventRecord {

            //--- Properties ---
            public string Id { get; set; }
            public string EventType { get; set; }
            public string Message { get; set; }
        }

        //--- Fields ---
        private string _contentCreatedTopicArn;
        private string _contentUpdatedTopicArn;
        private string _contentDeletedTopicArn;
        private IAmazonSimpleNotificationService _snsClient;

        //--- Constructors ---
        public Function() : base(new LambdaSharp.Serialization.LambdaSystemTextJsonSerializer()) { }

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // read configuration
            _contentCreatedTopicArn = config.ReadText("ContentCreatedTopicArn");
            _contentUpdatedTopicArn = config.ReadText("ContentUpdatedTopicArn");
            _contentDeletedTopicArn = config.ReadText("ContentDeletedTopicArn");

            // initialize clients
            _snsClient = new AmazonSimpleNotificationServiceClient();
        }

        public override async Task<KinesisFirehoseResponse> ProcessMessageAsync(KinesisFirehoseEvent request) {
            LogInfo($"Received {request.Records.Count:N0} Kinesis Stream records");

            // emit all records
            var processedRecords = await Task.WhenAll(request.Records.Select(async record => {
                var eventBody = Encoding.UTF8.GetString(Convert.FromBase64String(record.Base64EncodedData));
                var eventRecord = JsonSerializer.Deserialize<EventRecord>(eventBody);

                // determine best SNS topic based on event type
                var topicArn = eventRecord.EventType switch {
                    "Create" => _contentCreatedTopicArn,
                    "Update" => _contentUpdatedTopicArn,
                    "Delete" => _contentDeletedTopicArn,

                    // TODO: better alternative is to send unrecognized events to a Dead-Letter queue
                    var eventType => throw new InvalidOperationException($"Unrecognized event type: '{eventType}'")
                };

                // emit event
                await _snsClient.PublishAsync(topicArn, eventBody);

                // return
                return new KinesisFirehoseResponse.FirehoseRecord {
                    RecordId = record.RecordId,
                    Result = KinesisFirehoseResponse.TRANSFORMED_STATE_OK,
                    Base64EncodedData = record.Base64EncodedData
                };
            }));
            return new KinesisFirehoseResponse {
                Records = processedRecords.ToList()
            };
        }
    }
}
