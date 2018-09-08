﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Fx.Portability;
using Microsoft.Fx.Portability.ObjectModel;
using PortabilityService.Functions.DependencyInjection;
using PortabilityService.WorkflowManagement;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace PortabilityService.Functions
{
    public static class Analyze
    {
        [FunctionName("analyze")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestMessage req,
            [Queue("%WorkflowQueueName%")]ICollector<WorkflowQueueMessage> workflowMessageQueue,
            [Inject]IStorage storage,
            ILogger log)
        {
            var analyzeRequest = await DeserializeRequest(req.Content);

            if (analyzeRequest == null)
            {
                log.LogError("invalid request");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var submissionId = Guid.NewGuid().ToString();
            log.LogInformation("Created submission id {SubmissionId}", submissionId);

            try
            {
                await storage.SaveRequestToBlobAsync(analyzeRequest, submissionId);
            }
            catch (Exception ex)
            {
                log.LogError("Error occurs when saving analyze request to storage for submission {submissionId}: {exception}", submissionId, ex);
                return req.CreateErrorResponse(HttpStatusCode.InternalServerError, "Error occurs when saving analyze request to storage for submission");
            }

            var msg = WorkflowManager.GetFirstStage(submissionId);

            workflowMessageQueue.Add(msg);
            log.LogInformation("Queuing new message {SubmissionId}, stage {Stage}", msg.SubmissionId, msg.Stage);

            return ResponseWithServiceHeaders(req, submissionId);
        }

        public static async Task<AnalyzeRequest> DeserializeRequest(HttpContent content)
        {
            try
            {
                var stream = await content.ReadAsStreamAsync();
                return DataExtensions.DecompressToObject<AnalyzeRequest>(stream);
            }
            catch
            {
                return null;
            }
        }

        public static HttpResponseMessage ResponseWithServiceHeaders(HttpRequestMessage request, string submissionId)
        {
            var accessKey = GetAccessKey(submissionId);
            var reportUrl = new Uri(request.RequestUri, $"/api/report/{submissionId}");
            var content = new AnalyzeResponse
            {
                ResultAuthToken = accessKey,
                ResultUrl = reportUrl,
                SubmissionId = submissionId
            };

            var response = request.CreateResponse(HttpStatusCode.Created, content);
            response.Headers.Location = reportUrl;
            response.Headers.Add(typeof(EndpointStatus).Name, EndpointStatus.Alive.ToString());

            return response;
        }

        public static string GetAccessKey(string submissionId)
        {
            // TODO generate and persist a new unique key
            // the reversed submissionId is used only so the mock Results
            // function can validate it without consulting an external source
            var chars = submissionId.ToCharArray();
            Array.Reverse(chars);

            return new string(chars);
        }
    }
}