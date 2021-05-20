using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.KinesisEvents;
using Amazon.SimpleNotificationService;
using LambdaSharp;

namespace ServerlessPatterns.KinesisStream.EmitFunction {

    public sealed class Function : ALambdaFunction<KinesisEvent, string> {

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

        public override async Task<string> ProcessMessageAsync(KinesisEvent request) {
            LogInfo($"Received {request.Records.Count:N0} Kinesis Stream records");

            // emit all records
            foreach(var record in request.Records) {
                RunTask(async () => {
                    var eventBody = Encoding.UTF8.GetString(record.Kinesis.Data.ToArray());
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
                });
            }
            return "Ok";
        }
    }
}
