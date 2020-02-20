using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using Microsoft.CSharp.RuntimeBinder;

namespace Codice.Client.IssueTracker.HacknPlan
{
    public class HacknPlanExtension : IPlasticIssueTrackerExtension
    {
        public static readonly int MAX_WORKITEMS_PER_QUERY = 20;
        public static readonly int MAX_QUERIES_PER_SECOND = 5;
        public static readonly int MAX_QUERY_TIME_MS = 1000 / MAX_QUERIES_PER_SECOND;

        public static readonly string USERNAME_KEY = "Username";
        public static readonly string BRANCH_PREFIX_KEY = "Branch Prefix";
        public static readonly string API_SECRET_KEY = "API Secret";
        public static readonly string PROJECT_ID_KEY = "Project Id";
        public static readonly string PENDING_STAGE_ID_KEY = "Pending Tasks Stage Id";
        public static readonly string OPEN_STAGE_ID_KEY = "Open Tasks Stage Id";
        public static readonly string IGNORE_BACKLOG_KEY = "Ignore Backlog";
        public static readonly string IGNORED_BOARD_IDS_KEY = "Ignored Board Ids";
        
        IssueTrackerConfiguration _config;

        static readonly ILog _log = LogManager.GetLogger("HacknPlan");

        HttpClient httpClient = new HttpClient();

        private string _projectId;
        private int _userId;
        private string _userName;
        private string _stageId;
        private bool _ignoreBacklog;
        private List<int> _ignoredBoardIds = new List<int>();

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
            _ignoreBacklog = Convert.ToBoolean(_config.GetValue(IGNORE_BACKLOG_KEY));
            try
            {
                _ignoredBoardIds = config.GetValue(IGNORED_BOARD_IDS_KEY).Split(',').Select(x => Int32.Parse(x)).ToList();
            }  catch (NullReferenceException)
            {
                _log.Error("Parsing Ignored Board Ids failed");
            }
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
            } catch (RuntimeBinderException e)
            {
                _log.Error(e.Message);

                _connected = false;
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
            var queryParams = new Dictionary<string, string>();

            int totalCount;
            int currentOffset = 0;

            if (!string.IsNullOrEmpty(assignee))
                queryParams["userId"] = $"{_userId}";

            queryParams["stageId"] = $"{ _stageId}";
            queryParams["offset"] = $"{0}";
            queryParams["limit"] = $"{MAX_WORKITEMS_PER_QUERY}";

            do
            {
                queryParams["offset"] = $"{currentOffset}";

                var query = $"projects/{_projectId}/workitems?{queryParams.AsUriQuery()}";
                _log.Info($"Querying work items from Uri {httpClient.BaseAddress + query}");

                var lastQueryTime = DateTime.UtcNow;

                var data = GetJsonFromApi(query);

                if (data == null)
                {
                    _log.Error("Querying work items failed, returning items received");
                    return taskList;
                }

                totalCount = data.totalCount;
                int queryCount = 0;

                foreach (var workItem in data.items)
                {
                    currentOffset++;
                    queryCount++;

                    var hasBoard = HasDynamicProperty(workItem, "board");

                    // Ignore backlog if set
                    if (_ignoreBacklog && !hasBoard)
                        continue;

                    // Ignore specific boardId's
                    if (hasBoard && _ignoredBoardIds.Contains((int) workItem.board.boardId))
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

                    var timeSinceLastQuery = (int) Math.Ceiling((DateTime.UtcNow - lastQueryTime).TotalMilliseconds);
                    var timeToWait = MAX_QUERY_TIME_MS - timeSinceLastQuery;

                    if (timeToWait > 0)
                        System.Threading.Thread.Sleep(timeToWait);
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
            var workItem = GetWorkItemAsJson(taskId);

            // Workaround the v0 API bug which clears out some fields (like description) when patching
            // by setting all values in the patch call, even though we only want to change the stageId
            var newWorkItem = new
            {
                title = workItem.title,
                description = workItem.description,
                parentId = workItem.parentId,
                isStory = workItem.isStory,
                categoryId = workItem.categoryId,
                estimatedCost = workItem.estimatedCost,
                importanceLevelId = workItem.importanceLevelId,
                boardId = workItem.boardId,
                designElementId = workItem.designElementId,
                stageId = _config.GetValue(OPEN_STAGE_ID_KEY),
                startDate = workItem.startDate,
                dueDate = workItem.dueDate,
                boardIndex = workItem.boardIndex,
                designElementIndex = workItem.designElementIndex
            };

            var requestBody = JsonConvert.SerializeObject(newWorkItem);

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

    public static class DictionaryExtensions
    {
        public static string AsUriQuery(this Dictionary<string, string> queryParams) =>
            queryParams.Select(x => $"{x.Key}={x.Value}")
                .Aggregate((current, next) => $"{current}&{next}");
    }
}