﻿using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace Codice.Client.IssueTracker.HacknPlan
{
    public class HacknPlanExtension : IPlasticIssueTrackerExtension
    {
        public static readonly string USERNAME_KEY = "Username";
        public static readonly string BRANCH_PREFIX_KEY = "Branch Prefix";
        public static readonly string API_SECRET_KEY = "API Secret";
        public static readonly string PROJECT_ID_KEY = "Project Id";
        public static readonly string PENDING_STAGE_ID_KEY = "Pending Tasks Stage ID";
        public static readonly string OPEN_STAGE_ID_KEY = "Open Tasks Stage ID";
        public static readonly string IGNORE_BACKLOG_KEY = "Ignore Backlog";

        IssueTrackerConfiguration _config;

        static readonly ILog _log = LogManager.GetLogger("HacknPlan");

        HttpClient httpClient = new HttpClient();

        private string _projectId;
        private int _userId;
        private string _userName;
        private string _stageId;

        private bool _connected;
        public bool Connected {
            get {
                if (!_connected)
                {
                    _log.Error("Calling interface methods while not connected");
                    return _connected;
                }

                return _connected;
            }
        }

        internal HacknPlanExtension(IssueTrackerConfiguration config)
        {
            _config = config;
            _log.Info("HacknPlan issue tracker extension initialized");
            _projectId = config.GetValue(PROJECT_ID_KEY);
            _stageId = config.GetValue(PENDING_STAGE_ID_KEY);
        }

        public void Connect()
        {
            httpClient = GetHttpClient(_config);

            var me = GetJsonFromApi("users/me");

            try
            {
                _userId = me.id;
                _userName = me.username;

                _connected = true;
            } catch (AggregateException ae)
            {
                ae.Handle((x) =>
                {
                    if (x is NullReferenceException)
                    {
                        _log.Error(x.Message);

                        _connected = false;

                        return true;
                    }

                    _log.Error(ae.Message);

                    return false;
                });
            }
        }

        public void Disconnect()
        {
            httpClient.Dispose();
        }

        public string GetExtensionName()
        {
            return "HacknPlan";
        }

        public List<PlasticTask> GetPendingTasks()
        {
            if (!Connected) return new List<PlasticTask>();

            return GetPendingTasksInternal();
        }

        public List<PlasticTask> GetPendingTasks(string assignee)
        {
            if (!Connected) return new List<PlasticTask>();

            return GetPendingTasksInternal(assignee);
        }

        public List<PlasticTask> GetPendingTasksInternal(string assignee = "")
        {
            var taskList = new List<PlasticTask>();

            var userIdQueryItem = string.IsNullOrEmpty(assignee) ? "" : $"&userId={_userId}";

            int totalCount;
            int currentOffset = 0;

            do
            {
                var query = $"projects/{_projectId}/workitems?stageId={_stageId}&offset={currentOffset}{userIdQueryItem}";
                _log.Info(query);

                var data = GetJsonFromApi(query);
                _log.Info(data?.ToString());

                if (data == null)
                    return taskList;

                totalCount = data.totalCount;

                foreach (var workItem in data.items)
                {
                    currentOffset++;

                    if (Convert.ToBoolean(_config.GetValue(IGNORE_BACKLOG_KEY)) && !HasDynamicProperty(workItem, "board"))
                        continue;

                    taskList.Add(new PlasticTask()
                    {
                        CanBeLinked = true,
                        Id = workItem.workItemId,
                        Title = workItem.title,
                        Description = workItem.description,
                        Owner = workItem.user.username,
                        Status = workItem.stage.status
                    });
                }
            } while (totalCount > currentOffset);

            return taskList;
        }

        private dynamic GetJsonFromApi(string query)
        {
            string body = "";
            try
            {
                body = httpClient.GetStringAsync(query).Result;
            }
            catch (AggregateException ae)
            {
                ae.Handle((x) =>
                {
                    if (x is HttpRequestException)
                    {
                        _log.Error($"Task API request failed: {x.Message}");
                        return true;
                    }

                    return false;
                });

                return default;
            }

            return JsonConvert.DeserializeObject<dynamic>(body);
        }

        public PlasticTask GetTaskForBranch(string fullBranchName)
        {
            if (!Connected) return default;

            var id = GetTaskIdFromBranchName(fullBranchName);
            if (string.IsNullOrEmpty(id)) return default;

            var workItem = GetWorkItemAsJson(id);
            if (workItem == null) return default;
            _log.Info(workItem.ToString());

            return new PlasticTask() {
                CanBeLinked = true,
                Id = id,
                Title = workItem.title,
                Description = workItem.description,
                Owner = workItem.user.username,
                Status = workItem.stage.status
            };
        }

        private dynamic GetWorkItemAsJson(string id)
        {
            return GetJsonFromApi($"projects/{_projectId}/workitems/{id}");
        }

        private string GetTaskIdFromBranchName(string fullBranchName)
        {
            try
            {
                return fullBranchName.Split('_').Last().Split('/').First();
            }
            catch (NullReferenceException)
            {
                _log.Error($"Could not parse task id from {fullBranchName}");
            }

            return null;
        }

        public Dictionary<string, PlasticTask> GetTasksForBranches(List<string> fullBranchNames)
        {
            return new Dictionary<string, PlasticTask>();
        }

        public List<PlasticTask> LoadTasks(List<string> taskIds)
        {
            throw new NotImplementedException();
        }

        public void LogCheckinResult(PlasticChangeset changeset, List<PlasticTask> tasks)
        {
        }

        public void MarkTaskAsOpen(string taskId, string assignee)
        {
            var requestBody = JsonConvert.SerializeObject(new { stageId = _config.GetValue(OPEN_STAGE_ID_KEY) });

            var request = new HttpRequestMessage()
            {
                Method = new HttpMethod("PATCH"),
                RequestUri = new Uri($"{httpClient.BaseAddress}projects/{_projectId}/workItems/{taskId}"),
                Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json")
            };

            request.Headers.Authorization = httpClient.DefaultRequestHeaders.Authorization;

            try
            {
                var response = httpClient.SendAsync(request).Result;
            } catch (AggregateException ae)
            {
                ae.Handle(x =>
                {
                    if (x is HttpRequestException)
                    {
                        _log.Error(x.Message);

                        return true;
                    }

                    return false;
                });
            }
        }

        public void OpenTaskExternally(string taskId)
        {
            Process.Start($"https://app.hacknplan.com/p/72274/kanban?taskId={taskId}");
        }

        public bool TestConnection(IssueTrackerConfiguration configuration)
        {
            var httpClient = GetHttpClient(configuration);

            try
            {
                var meJson = httpClient.GetStringAsync("users/me").Result;
                var me = JsonConvert.DeserializeObject<dynamic>(meJson);
                MessageBox.Show($"Username: {me.username}\nUser Id: {me.id}", "Connection Succesful", MessageBoxButtons.OK);
                return true;
            }
            catch (Exception e)
            {
                var msg = e.Message;
                if (e is AggregateException)
                    msg = e.InnerException.Message;

                MessageBox.Show(msg, "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return false;
            }

        }

        private HttpClient GetHttpClient(IssueTrackerConfiguration configuration)
        {
            var httpClient = new HttpClient();

            httpClient.BaseAddress = new Uri($@"https://api.hacknplan.com/v0/");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", configuration.GetValue(API_SECRET_KEY));
            httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue() { NoCache = true };

            return httpClient;
        }

        public void UpdateLinkedTasksToChangeset(PlasticChangeset changeset, List<string> tasks)
        {
        }

        public bool HasDynamicProperty(dynamic dynamicObject, string propertyName) =>
            (dynamicObject as JObject).ContainsKey(propertyName);
    }
}