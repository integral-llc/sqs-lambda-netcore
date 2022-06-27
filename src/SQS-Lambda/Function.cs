using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Newtonsoft.Json;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SQS_Lambda
{

    public class Function
    {
        private static string _twilioAccountSid = "";
        private static string _twilioAuthToken = "";
        private static string _fromPhoneNumber = "";
        private static string _outputQueueUrl = "";

        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public Function()
        {
            AWSSDKHandler.RegisterXRayForAllServices();

            _twilioAccountSid = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID") ?? "NA";
            _twilioAuthToken = Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN") ?? "NA";
            _fromPhoneNumber = Environment.GetEnvironmentVariable("TWILIO_FROM_PHONE_NUMBER") ?? "NA";
            _outputQueueUrl = Environment.GetEnvironmentVariable("AWS_SQS_OUTPUT_QUEUE_URL") ?? "NA";
        }


        /// <summary>
        /// This method is called for every Lambda invocation. This method takes in an SQS event object and can be used 
        /// to respond to SQS messages.
        /// </summary>
        /// <param name="event"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task FunctionHandler(SQSEvent @event, ILambdaContext context)
        {
            if (_twilioAccountSid == "NA")
            {
                context.Logger.Log("Twilio environment variables are not set.");
                return;
            }

            TwilioClient.Init(_twilioAccountSid, _twilioAuthToken);
            var sqsClient = new AmazonSQSClient();

            foreach (var message in @event.Records)
            {
                await ProcessMessageAsync(message, sqsClient, context);
            }
        }

        private static readonly Regex RgPhone = new Regex(@"([0-9]{10})");

        private static async Task ProcessMessageAsync(SQSEvent.SQSMessage message,
            IAmazonSQS sqsClient,
            ILambdaContext context)
        {
            string messageToSend = message.Body;
            context.Logger.Log($"Processed message: {message.Body}");

            var res = RgPhone.Match(message.Body);
            string toPhoneNumber = "9284586564";
            if (res.Success)
            {
                toPhoneNumber = res.Groups[1].Value;
            }
            else
            {
                messageToSend += "\nFailed to extract phone number";
            }

            try
            {
                var msg = await MessageResource.CreateAsync(
                    body: messageToSend,
                    from: new PhoneNumber(_fromPhoneNumber),
                    to: new PhoneNumber("+1" + toPhoneNumber)
                );

                string twilioAnswer = JsonConvert.SerializeObject(msg);
                context.Logger.Log(twilioAnswer);

                await SendMessage(sqsClient, messageToSend, context);
                await SendMessage(sqsClient, twilioAnswer, context);
            }
            catch (Exception e)
            {
                context.Logger.Log(e.Message);
            }
        }

        private static async Task SendMessage(IAmazonSQS sqsClient, string messageBody, ILambdaContext context)
        {
            try
            {
                SendMessageResponse responseSendMsg =
                    await sqsClient.SendMessageAsync(_outputQueueUrl, messageBody);
                Console.WriteLine($"Message added to queue\n  {_outputQueueUrl}");
                Console.WriteLine($"HttpStatusCode: {responseSendMsg.HttpStatusCode}");

                context.Logger.Log($"Messages {messageBody} has been send to the output queue");
            }
            catch (Exception ex)
            {
                context.Logger.Log($"Could not send message {messageBody} to the output queue. Error: {ex}");
            }
        }
    }
}