using System;
using System.Collections.Generic;
using System.Text;
using Codice.Client.IssueTracker;

namespace Codice.Client.IssueTracker.HacknPlan
{
    class HacknPlanExtensionFactory : IPlasticIssueTrackerExtensionFactory
    {


        public IssueTrackerConfiguration GetConfiguration(IssueTrackerConfiguration storedConfiguration)
        {
            var workingMode = GetWorkingMode(storedConfiguration);

            var prefix = GetValidParameterValue(storedConfiguration, HacknPlanExtension.BRANCH_PREFIX_KEY, "task_");
            var apiSecret = GetValidParameterValue(storedConfiguration, HacknPlanExtension.API_SECRET_KEY, "");
            var projectId = GetValidParameterValue(storedConfiguration, HacknPlanExtension.PROJECT_ID_KEY, "");
            var pendingStageId = GetValidParameterValue(storedConfiguration, HacknPlanExtension.PENDING_STAGE_ID_KEY, "1");
            var openStageId = GetValidParameterValue(storedConfiguration, HacknPlanExtension.OPEN_STAGE_ID_KEY, "2");
            var ignoreBacklog = GetValidParameterValue(storedConfiguration, HacknPlanExtension.IGNORE_BACKLOG_KEY, "true");

            var branchPrefixParam = new IssueTrackerConfigurationParameter()
            {
                Name = HacknPlanExtension.BRANCH_PREFIX_KEY,
                Value = prefix,
                Type = IssueTrackerConfigurationParameterType.BranchPrefix,
                IsGlobal = true
            };

            var apiSecretParam = new IssueTrackerConfigurationParameter()
            {
                Name = HacknPlanExtension.API_SECRET_KEY,
                Value = apiSecret,
                Type = IssueTrackerConfigurationParameterType.Text,
                IsGlobal = true
            };

            var projectIdParam = new IssueTrackerConfigurationParameter()
            {
                Name = HacknPlanExtension.PROJECT_ID_KEY,
                Value = projectId,
                Type = IssueTrackerConfigurationParameterType.Text,
                IsGlobal = true
            };

            var pendingStageIdParam = new IssueTrackerConfigurationParameter()
            {
                Name = HacknPlanExtension.PENDING_STAGE_ID_KEY,
                Value = pendingStageId,
                Type = IssueTrackerConfigurationParameterType.Text,
                IsGlobal = true
            };

            var openStageIdParam = new IssueTrackerConfigurationParameter()
            {
                Name = HacknPlanExtension.OPEN_STAGE_ID_KEY,
                Value = openStageId,
                Type = IssueTrackerConfigurationParameterType.Text,
                IsGlobal = true
            };

            var ignoreBacklogParam = new IssueTrackerConfigurationParameter()
            {
                Name = HacknPlanExtension.IGNORE_BACKLOG_KEY,
                Value = ignoreBacklog,
                Type = IssueTrackerConfigurationParameterType.Boolean,
                IsGlobal = true
            };

            var parameters = new List<IssueTrackerConfigurationParameter>()
            {
                branchPrefixParam,
                apiSecretParam,
                projectIdParam,
                pendingStageIdParam,
                openStageIdParam,
                ignoreBacklogParam
            };

            return new IssueTrackerConfiguration(workingMode, parameters);
        }

        private string GetValidParameterValue(IssueTrackerConfiguration config, string paramName, string defaultValue)
        {
            string configValue = config?.GetValue(paramName);

            if (string.IsNullOrEmpty(configValue))
                return defaultValue;

            return configValue;
        }

        private ExtensionWorkingMode GetWorkingMode(IssueTrackerConfiguration config)
        {
            if (config == null)
                return ExtensionWorkingMode.TaskOnBranch;

            if (config.WorkingMode == ExtensionWorkingMode.None)
                return ExtensionWorkingMode.TaskOnBranch;

            return config.WorkingMode;
        }

        public IPlasticIssueTrackerExtension GetIssueTrackerExtension(IssueTrackerConfiguration configuration)
        {
            return new HacknPlanExtension(configuration);
        }

        public string GetIssueTrackerName()
        {
            return "HacknPlan Issue Tracker";
        }
    }
}
