﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
#if !NET45
using Microsoft.Extensions.Logging;
#endif
using DynamicExpresso;
using NetCasbin.Abstractions;
using NetCasbin.Effect;
using NetCasbin.Evaluation;
using NetCasbin.Model;
using NetCasbin.Persist;
using NetCasbin.Rbac;
using NetCasbin.Util;
using NetCasbin.Extensions;
using NetCasbin.Caching;

namespace NetCasbin
{
    /// <summary>
    /// CoreEnforcer defines the core functionality of an enforcer.
    /// </summary>
    public class CoreEnforcer : ICoreEnforcer
    {
        private IEffector _effector;
        private bool _enabled;

        protected string modelPath;
        protected Model.Model model;

        protected IAdapter adapter;
        protected IWatcher watcher;
        protected bool autoSave;
        protected bool autoBuildRoleLinks;
        protected bool autoNotifyWatcher;
        protected bool autoCleanEnforceCache = true;
        internal IExpressionHandler ExpressionHandler { get; private set; }

        private bool _enableCache;
        public IEnforceCache EnforceCache { get; private set; }
#if !NET45
        public ILogger Logger { get; set; }
#endif

        protected void Initialize()
        {
            _effector = new DefaultEffector();
            watcher = null;

            _enabled = true;
            autoSave = true;
            autoBuildRoleLinks = true;
            autoNotifyWatcher = true;
        }

        /// <summary>
        /// Creates a model.
        /// </summary>
        /// <returns></returns>
        [Obsolete("The method will be moved to Model class at next mainline version, you can see https://github.com/casbin/Casbin.NET/issues/52 to know more information.")]
        public static Model.Model NewModel()
        {
            var model = new Model.Model();
            return model;
        }

        /// <summary>
        /// Creates a model.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        [Obsolete("The method will be moved to Model class at next mainline version, you can see https://github.com/casbin/Casbin.NET/issues/52 to know more information.")]
        public static Model.Model NewModel(string text)
        {
            var model = new Model.Model();
            model.LoadModelFromText(text);
            return model;
        }

        /// <summary>
        /// Creates a model.
        /// </summary>
        /// <param name="modelPath">The path of the model file.</param>
        /// <param name="unused">Unused parameter, just for differentiating with  NewModel(String text).</param>
        /// <returns></returns>
        [Obsolete("The method will be moved to Model class at next mainline version, you can see https://github.com/casbin/Casbin.NET/issues/52 to know more information.")]
        public static Model.Model NewModel(string modelPath, string unused)
        {
            var model = new Model.Model();
            if (!string.IsNullOrEmpty(modelPath))
            {
                model.LoadModel(modelPath);
            }
            return model;
        }

        /// <summary>
        /// LoadModel reloads the model from the model CONF file. Because the policy is
        /// Attached to a model, so the policy is invalidated and needs to be reloaded by
        /// calling LoadPolicy().
        /// </summary>
        public void LoadModel()
        {
            model = NewModel();
            model.LoadModel(modelPath);
        }

        /// <summary>
        /// Gets the current model.
        /// </summary>
        /// <returns>The model of the enforcer.</returns>
        public Model.Model GetModel() => model;

        /// <summary>
        /// Sets the current model.
        /// </summary>
        /// <param name="model"></param>
        public void SetModel(Model.Model model)
        {
            this.model = model;
            ExpressionHandler = new ExpressionHandler(model);
            if (autoCleanEnforceCache)
            {
                EnforceCache?.Clear();
#if !NET45
                Logger?.LogInformation("Enforcer Cache, Cleared all enforce cache.");
#endif
            }
        }

        /// <summary>
        /// Gets the current adapter.
        /// </summary>
        /// <returns></returns>
        public IAdapter GetAdapter() => adapter;

        /// <summary>
        /// Sets an adapter.
        /// </summary>
        /// <param name="adapter"></param>
        public void SetAdapter(IAdapter adapter)
        {
            this.adapter = adapter;
        }

        /// <summary>
        /// Sets an watcher.
        /// </summary>
        /// <param name="watcher"></param>
        /// <param name="useAsync">Whether use async update callback.</param>
        public void SetWatcher(IWatcher watcher, bool useAsync = true)
        {
            this.watcher = watcher;
            if (useAsync)
            {
                watcher?.SetUpdateCallback(LoadPolicyAsync);
                return;
            }
            watcher?.SetUpdateCallback(LoadPolicy);
        }

        /// <summary>
        /// Sets the current role manager.
        /// </summary>
        /// <param name="roleManager"></param>
        public void SetRoleManager(IRoleManager roleManager)
        {
            SetRoleManager(PermConstants.DefaultRoleType, roleManager);
            ExpressionHandler.SetGFunctions();
        }

        /// <summary>
        /// Sets the current role manager.
        /// </summary>
        /// <param name="roleType"></param>
        /// <param name="roleManager"></param>
        public void SetRoleManager(string roleType, IRoleManager roleManager)
        {
            Assertion assertion = model.GetExistAssertion(PermConstants.Section.RoleSection, roleType);
            assertion.RoleManager = roleManager;
            if (autoBuildRoleLinks)
            {
                assertion.BuildRoleLinks();
            }
        }

        /// <summary>
        /// Sets the current effector.
        /// </summary>
        /// <param name="effector"></param>
        public void SetEffector(IEffector effector)
        {
            _effector = effector;
        }

        /// <summary>
        /// Sets an enforce cache.
        /// </summary>
        /// <param name="enforceCache"></param>
        public void SetEnforceCache(IEnforceCache enforceCache)
        {
            EnforceCache = enforceCache;
        }

        /// <summary>
        /// Clears all policy.
        /// </summary>
        public void ClearPolicy()
        {
            model.ClearPolicy();
            if (autoCleanEnforceCache)
            {
                EnforceCache?.Clear();
#if !NET45
                Logger?.LogInformation("Enforcer Cache, Cleared all enforce cache.");
#endif
            }
#if !NET45
            Logger?.LogInformation("Policy Management, Cleared all policy.");
#endif
        }

        /// <summary>
        /// Reloads the policy from file/database.
        /// </summary>
        public void LoadPolicy()
        {
            if (adapter is null)
            {
                return;
            }

            ClearPolicy();
            adapter.LoadPolicy(model);

            model.RefreshPolicyStringSet();
            model.SortPoliciesByPriority();

            if (autoBuildRoleLinks)
            {
                BuildRoleLinks();
            }
        }

        /// <summary>
        /// Reloads the policy from file/database.
        /// </summary>
        public async Task LoadPolicyAsync()
        {
            if (adapter is null)
            {
                return;
            }

            ClearPolicy();
            await adapter.LoadPolicyAsync(model);

            model.RefreshPolicyStringSet();
            model.SortPoliciesByPriority();

            if (autoBuildRoleLinks)
            {
                BuildRoleLinks();
            }
        }

        /// <summary>
        /// Reloads a filtered policy from file/database.
        /// </summary>
        /// <param name="filter">The filter used to specify which type of policy should be loaded.</param>
        /// <returns></returns>
        public bool LoadFilteredPolicy(Filter filter)
        {
            ClearPolicy();
            if (adapter is not IFilteredAdapter filteredAdapter)
            {
                throw new NotSupportedException("Filtered policies are not supported by this adapter.");
            }

            filteredAdapter.LoadFilteredPolicy(model, filter);

            model.RefreshPolicyStringSet();
            model.SortPoliciesByPriority();

            if (autoBuildRoleLinks)
            {
                BuildRoleLinks();
            }
            return true;
        }

        /// <summary>
        /// Reloads a filtered policy from file/database.
        /// </summary>
        /// <param name="filter">The filter used to specify which type of policy should be loaded.</param>
        /// <returns></returns>
        public async Task<bool> LoadFilteredPolicyAsync(Filter filter)
        {
            ClearPolicy();
            if (adapter is not IFilteredAdapter filteredAdapter)
            {
                throw new NotSupportedException("Filtered policies are not supported by this adapter.");
            }

            await filteredAdapter.LoadFilteredPolicyAsync(model, filter);

            model.RefreshPolicyStringSet();
            model.SortPoliciesByPriority();

            if (autoBuildRoleLinks)
            {
                BuildRoleLinks();
            }
            return true;
        }

        /// <summary>
        /// Returns true if the loaded policy has been filtered.
        /// </summary>
        /// <returns>if the loaded policy has been filtered.</returns>
        public bool IsFiltered()
        {
            if (adapter is IFilteredAdapter filteredAdapter)
            {
                return filteredAdapter.IsFiltered;
            }
            return false;
        }

        /// <summary>
        /// Saves the current policy (usually after changed with Casbin API)
        /// back to file/database.
        /// </summary>
        public void SavePolicy()
        {
            if (adapter is null)
            {
                return;
            }

            if (IsFiltered())
            {
                throw new InvalidOperationException("Cannot save a filtered policy");
            }
            adapter.SavePolicy(model);
            watcher?.Update();
        }

        /// <summary>
        /// Saves the current policy (usually after changed with Casbin API)
        /// back to file/database.
        /// </summary>
        public async Task SavePolicyAsync()
        {
            if (IsFiltered())
            {
                throw new InvalidOperationException("Cannot save a filtered policy");
            }
            await adapter.SavePolicyAsync(model);
            if (watcher is not null)
            {
                await watcher.UpdateAsync();
            }
        }

        /// <summary>
        /// Changes the enforcing state of Casbin, when Casbin is disabled,
        /// all access will be allowed by the enforce() function.
        /// </summary>
        /// <param name="enable"></param>
        public void EnableEnforce(bool enable)
        {
            _enabled = enable;
        }

        /// <summary>
        /// Controls whether to save a policy rule automatically to the
        /// adapter when it is added or removed.
        /// </summary>
        /// <param name="autoSave"></param>
        public void EnableAutoSave(bool autoSave)
        {
            this.autoSave = autoSave;
        }

        /// <summary>
        /// Controls whether to save a policy rule automatically
        /// to the adapter when it is added or removed.
        /// </summary>
        /// <param name="autoBuildRoleLinks">Whether to automatically build the role links.</param>
        public void EnableAutoBuildRoleLinks(bool autoBuildRoleLinks)
        {
            this.autoBuildRoleLinks = autoBuildRoleLinks;
        }

        /// <summary>
        /// Controls whether to save a policy rule automatically
        /// notify the Watcher when it is added or removed.
        /// </summary>
        /// <param name="autoNotifyWatcher">Whether to automatically notify watcher.</param>
        public void EnableAutoNotifyWatcher(bool autoNotifyWatcher)
        {
            this.autoNotifyWatcher = autoNotifyWatcher;
        }

        public void EnableCache(bool enableCache)
        {
            _enableCache = enableCache;
        }

        public void EnableAutoCleanEnforceCache(bool autoCleanEnforceCache)
        {
            this.autoCleanEnforceCache = autoCleanEnforceCache;
        }

        /// <summary>
        /// Manually rebuilds the role inheritance relations.
        /// </summary>
        public void BuildRoleLinks()
        {
            model.BuildRoleLinks();
        }

        #region Enforce
        /// <summary>
        /// Decides whether a "subject" can access a "object" with the operation
        /// "action", input parameters are usually: (sub, obj, act).
        /// </summary>
        /// <param name="requestValues">The request needs to be mediated, usually an array of strings, 
        /// can be class instances if ABAC is used.</param>
        /// <returns>Whether to allow the request.</returns>
        public bool Enforce(params object[] requestValues)
        {
            if (_enabled is false)
            {
                return true;
            }

            if (_enableCache is false)
            {
                return InternalEnforce(requestValues);
            }

            if (requestValues.Any(requestValue => requestValue is not string))
            {
                return InternalEnforce(requestValues);
            }

            string key = string.Join("$$", requestValues);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            if (EnforceCache.TryGetResult(requestValues, key, out bool cachedResult))
            {
#if !NET45
                Logger?.LogEnforceCachedResult(requestValues, cachedResult);
#endif
                return cachedResult;
            }

            bool result = InternalEnforce(requestValues);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            EnforceCache.TrySetResult(requestValues, key, result);
            return result;
        }

        /// <summary>
        /// Decides whether a "subject" can access a "object" with the operation
        /// "action", input parameters are usually: (sub, obj, act).
        /// </summary>
        /// <param name="requestValues">The request needs to be mediated, usually an array of strings, 
        /// can be class instances if ABAC is used.</param>
        /// <returns>Whether to allow the request.</returns>
        public async Task<bool> EnforceAsync(params object[] requestValues)
        {
            if (_enabled is false)
            {
                return true;
            }

            if (_enableCache is false)
            {
                return await InternalEnforceAsync(requestValues);
            }

            if (requestValues.Any(requestValue => requestValue is not string))
            {
                return await InternalEnforceAsync(requestValues);
            }

            string key = string.Join("$$", requestValues);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            bool? tryGetCachedResult = await EnforceCache.TryGetResultAsync(requestValues, key);
            if (tryGetCachedResult.HasValue)
            {
                bool cachedResult = tryGetCachedResult.Value;
#if !NET45
                Logger?.LogEnforceCachedResult(requestValues, cachedResult);
#endif
                return cachedResult;
            }

            bool result = await InternalEnforceAsync(requestValues);

            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            await EnforceCache.TrySetResultAsync(requestValues, key, result);
            return result;
        }

        /// <summary>
        /// Decides whether a "subject" can access a "object" with the operation
        /// "action", input parameters are usually: (sub, obj, act).
        /// </summary>
        /// <param name="matcher">The custom matcher.</param>
        /// <param name="requestValues">The request needs to be mediated, usually an array of strings,
        /// can be class instances if ABAC is used.</param>
        /// <returns>Whether to allow the request.</returns>
        public bool EnforceWithMatcher(string matcher, params object[] requestValues)
        {
            if (_enabled is false)
            {
                return true;
            }

            if (string.IsNullOrEmpty(matcher))
            {
                throw new ArgumentException($"'{nameof(matcher)}' cannot be null or empty.", nameof(matcher));
            }

            if (requestValues is null)
            {
                throw new ArgumentNullException(nameof(requestValues));
            }

            if (_enableCache is false)
            {
                return InternalEnforce(requestValues, matcher);
            }

            if (requestValues.Any(requestValue => requestValue is not string))
            {
                return InternalEnforce(requestValues, matcher);
            }

            string key = string.Join("$$", requestValues);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            if (EnforceCache.TryGetResult(requestValues, key, out bool cachedResult))
            {
#if !NET45
                Logger?.LogEnforceCachedResult(requestValues, cachedResult);
#endif
                return cachedResult;
            }

            bool result = InternalEnforce(requestValues, matcher);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            EnforceCache.TrySetResult(requestValues, key, result);
            return result;
        }

        /// <summary>
        /// Decides whether a "subject" can access a "object" with the operation
        /// "action", input parameters are usually: (sub, obj, act).
        /// </summary>
        /// <param name="requestValues">The request needs to be mediated, usually an array of strings, 
        /// can be class instances if ABAC is used.</param>
        /// <returns>Whether to allow the request.</returns>
        public async Task<bool> EnforceWithMatcherAsync(string matcher, params object[] requestValues)
        {
            if (_enabled is false)
            {
                return true;
            }

            if (string.IsNullOrEmpty(matcher))
            {
                throw new ArgumentException($"'{nameof(matcher)}' cannot be null or empty.", nameof(matcher));
            }

            if (requestValues is null)
            {
                throw new ArgumentNullException(nameof(requestValues));
            }

            if (_enableCache is false)
            {
                return await InternalEnforceAsync(requestValues, matcher);
            }

            if (requestValues.Any(requestValue => requestValue is not string))
            {
                return await InternalEnforceAsync(requestValues, matcher);
            }

            string key = string.Join("$$", requestValues);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            bool? tryGetCachedResult = await EnforceCache.TryGetResultAsync(requestValues, key);
            if (tryGetCachedResult.HasValue)
            {
                bool cachedResult = tryGetCachedResult.Value;
#if !NET45
                Logger?.LogEnforceCachedResult(requestValues, cachedResult);
#endif
                return cachedResult;
            }

            bool result = await InternalEnforceAsync(requestValues, matcher);

            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            await EnforceCache.TrySetResultAsync(requestValues, key, result);
            return result;
        }
        #endregion

        #region EnforceEx
        /// <summary>
        /// Explains enforcement by informing matched rules
        /// </summary>
        /// <param name="requestValues">The request needs to be mediated, usually an array of strings, 
        /// can be class instances if ABAC is used.</param>
        /// <returns>Whether to allow the request and explains.</returns>
#if !NET45
        public (bool Result, IEnumerable<IEnumerable<string>> Explains)
            EnforceEx(params object[] requestValues)
        {
            var explains = new List<IEnumerable<string>>();
            if (_enabled is false)
            {
                return (true, explains);
            }

            if (_enableCache is false)
            {
                return (InternalEnforce(requestValues, null, explains), explains);
            }

            if (requestValues.Any(requestValue => requestValue is not string))
            {
                return (InternalEnforce(requestValues, null, explains), explains);
            }

            string key = string.Join("$$", requestValues);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            if (EnforceCache.TryGetResult(requestValues, key, out bool cachedResult))
            {
                Logger?.LogEnforceCachedResult(requestValues, cachedResult);
                return (cachedResult, explains);
            }

            bool result = InternalEnforce(requestValues, null, explains);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            EnforceCache.TrySetResult(requestValues, key, result);
            return (result, explains);
        }
#else
        public Tuple<bool, IEnumerable<IEnumerable<string>>>
            EnforceEx(params object[] requestValues)
        {
            var explains = new List<IEnumerable<string>>();
            bool result = InternalEnforce(requestValues, null, explains);
            return new Tuple<bool, IEnumerable<IEnumerable<string>>>(result, explains);
        }
#endif

        /// <summary>
        /// Explains enforcement by informing matched rules
        /// </summary>
        /// <param name="requestValues">The request needs to be mediated, usually an array of strings, 
        /// can be class instances if ABAC is used.</param>
        /// <returns>Whether to allow the request and explains.</returns>
#if !NET45
        public async Task<(bool Result, IEnumerable<IEnumerable<string>> Explains)>
            EnforceExAsync(params object[] requestValues)
        {
            var explains = new List<IEnumerable<string>>();
            if (_enabled is false)
            {
                return (true, explains);
            }

            if (_enableCache is false)
            {
                return (await InternalEnforceAsync(requestValues, null, explains), explains);
            }

            if (requestValues.Any(requestValue => requestValue is not string))
            {
                return (await InternalEnforceAsync(requestValues, null, explains), explains);
            }

            string key = string.Join("$$", requestValues);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            if (EnforceCache.TryGetResult(requestValues, key, out bool cachedResult))
            {
                Logger?.LogEnforceCachedResult(requestValues, cachedResult);
                return (cachedResult, explains);
            }

            bool result = await InternalEnforceAsync(requestValues, null, explains);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            await EnforceCache.TrySetResultAsync(requestValues, key, result);
            return (result, explains);
        }
#else
        public async Task<Tuple<bool, IEnumerable<IEnumerable<string>>>>
            EnforceExAsync(params object[] requestValues)
        {
            var explains = new List<IEnumerable<string>>();
            bool result = await InternalEnforceAsync(requestValues, null, explains);
            return new Tuple<bool, IEnumerable<IEnumerable<string>>>(result, explains);
        }
#endif

        /// <summary>
        /// Explains enforcement by informing matched rules
        /// </summary>
        /// <param name="matcher">The custom matcher.</param>
        /// <param name="requestValues">The request needs to be mediated, usually an array of strings, 
        /// can be class instances if ABAC is used.</param>
        /// <returns>Whether to allow the request and explains.</returns>
#if !NET45
        public (bool Result, IEnumerable<IEnumerable<string>> Explains)
            EnforceExWithMatcher(string matcher, params object[] requestValues)
        {
            var explains = new List<IEnumerable<string>>();
            if (_enabled is false)
            {
                return (true, explains);
            }

            if (_enableCache is false)
            {
                return (InternalEnforce(requestValues, matcher, explains), explains);
            }

            if (requestValues.Any(requestValue => requestValue is not string))
            {
                return (InternalEnforce(requestValues, matcher, explains), explains);
            }

            string key = string.Join("$$", requestValues);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            if (EnforceCache.TryGetResult(requestValues, key, out bool cachedResult))
            {
                Logger?.LogEnforceCachedResult(requestValues, cachedResult);
                return (cachedResult, explains);
            }

            bool result = InternalEnforce(requestValues, matcher, explains);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            EnforceCache.TrySetResult(requestValues, key, result);
            return (result, explains);
        }
#else
        public Tuple<bool, IEnumerable<IEnumerable<string>>>
            EnforceExWithMatcher(string matcher, params object[] requestValues)
        {
            var explains = new List<IEnumerable<string>>();
            bool result = InternalEnforce(requestValues, matcher, explains);
            return new Tuple<bool, IEnumerable<IEnumerable<string>>>(result, explains);
        }
#endif

        /// <summary>
        /// Explains enforcement by informing matched rules
        /// </summary>
        /// <param name="matcher">The custom matcher.</param>
        /// <param name="requestValues">The request needs to be mediated, usually an array of strings, 
        /// can be class instances if ABAC is used.</param>
        /// <returns>Whether to allow the request and explains.</returns>
#if !NET45
        public async Task<(bool Result, IEnumerable<IEnumerable<string>> Explains)>
            EnforceExWithMatcherAsync(string matcher, params object[] requestValues)
        {
            var explains = new List<IEnumerable<string>>();
            if (_enabled is false)
            {
                return (true, explains);
            }

            if (_enableCache is false)
            {
                return (await InternalEnforceAsync(requestValues, matcher, explains), explains);
            }

            if (requestValues.Any(requestValue => requestValue is not string))
            {
                return (await InternalEnforceAsync(requestValues, matcher, explains), explains);
            }

            string key = string.Join("$$", requestValues);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            if (EnforceCache.TryGetResult(requestValues, key, out bool cachedResult))
            {
                Logger?.LogEnforceCachedResult(requestValues, cachedResult);
                return (cachedResult, explains);
            }

            bool result = await InternalEnforceAsync(requestValues, matcher, explains);
            EnforceCache ??= new ReaderWriterEnforceCache(new ReaderWriterEnforceCacheOptions());
            await EnforceCache.TrySetResultAsync(requestValues, key, result);
            return (result, explains);
        }
#else
        public async Task<Tuple<bool, IEnumerable<IEnumerable<string>>>>
            EnforceExWithMatcherAsync(string matcher, params object[] requestValues)
        {
            var explains = new List<IEnumerable<string>>();
            bool result = await InternalEnforceAsync(requestValues, matcher, explains);
            return new Tuple<bool, IEnumerable<IEnumerable<string>>>(result, explains);
        }
#endif
        #endregion

        /// <summary>
        /// Decides whether a "subject" can access a "object" with the operation
        /// "action", input parameters are usually: (sub, obj, act).
        /// </summary>
        /// <param name="requestValues">The request needs to be mediated, usually an array of strings, 
        /// can be class instances if ABAC is used.</param>
        /// <param name="matcher">The custom matcher.</param>
        /// <param name="explains"></param>
        /// <returns>Whether to allow the request.</returns>
        private Task<bool> InternalEnforceAsync(IReadOnlyList<object> requestValues, string matcher = null, ICollection<IEnumerable<string>> explains = null)
        {
            return Task.FromResult(InternalEnforce(requestValues, matcher, explains));
        }

        /// <summary>
        /// Decides whether a "subject" can access a "object" with the operation
        /// "action", input parameters are usually: (sub, obj, act).
        /// </summary>
        /// <param name="requestValues">The request needs to be mediated, usually an array of strings, 
        /// can be class instances if ABAC is used.</param>
        /// <param name="matcher">The custom matcher.</param>
        /// <param name="explains"></param>
        /// <returns>Whether to allow the request.</returns>
        private bool InternalEnforce(IReadOnlyList<object> requestValues, string matcher = null, ICollection<IEnumerable<string>> explains = null)
        {
            EnforcerVariableStorage variableStorage =
                EnforcerVariableStorage.Init(matcher, explains, model, ExpressionHandler);
            
            if (variableStorage.RequestTokenCount != requestValues.Count)
            {
                throw new ArgumentException($"Invalid request size: expected {variableStorage.RequestTokenCount}, got {requestValues.Count}.");
            }

            ExpressionHandler.SetRequestParameters(requestValues);

            bool finalResult = false;
            IChainEffector chainEffector = _effector as IChainEffector;
            if (chainEffector is not null)
            {
                return ProcessChainEffector(variableStorage, finalResult, requestValues, explains);
            }

            int hitPolicyIndex;
            if (variableStorage.PolicyCount != 0)
            {
                Effect.Effect[] policyEffects = new Effect.Effect[variableStorage.PolicyCount];

                for (int i = 0; i < variableStorage.PolicyCount; i++)
                {
                    IReadOnlyList<string> policyValues = variableStorage.PolicyList[i];

                    if (variableStorage.PolicyTokenCount != policyValues.Count)
                    {
                        throw new ArgumentException($"Invalid policy size: expected {variableStorage.PolicyTokenCount}, got {policyValues.Count}.");
                    }

                    ExpressionHandler.SetPolicyParameters(policyValues);

                    bool expressionResult;

                    if (variableStorage.HasEval)
                    {
                        string expressionStringWithRule = RewriteEval(variableStorage.ExpressionString, ExpressionHandler.PolicyTokens, policyValues);
                        expressionResult = ExpressionHandler.Invoke(expressionStringWithRule, requestValues);
                    }
                    else
                    {
                        expressionResult = ExpressionHandler.Invoke(variableStorage.ExpressionString, requestValues);
                    }

                    var nowEffect = GetEffect(expressionResult);

                    if (nowEffect is Effect.Effect.Indeterminate)
                    {
                        policyEffects[i] = nowEffect;
                        continue;
                    }

                    if (ExpressionHandler.Parameters.TryGetValue("p_eft", out Parameter parameter))
                    {
                        string policyEffect = parameter.Value as string;
                        nowEffect = policyEffect switch
                        {
                            "allow" => Effect.Effect.Allow,
                            "deny" => Effect.Effect.Deny,
                            _ => Effect.Effect.Indeterminate
                        };
                    }

                    policyEffects[i] = nowEffect;

                    if (variableStorage.Effect.Equals(PermConstants.PolicyEffect.Priority))
                    {
                        break;
                    }
                }

                finalResult = _effector.MergeEffects(variableStorage.Effect, policyEffects, null, out hitPolicyIndex);
            }
            else
            {
                if (variableStorage.HasEval)
                {
                    throw new ArgumentException("Please make sure rule exists in policy when using eval() in matcher");
                }

                IReadOnlyList<string> policyValues = Enumerable.Repeat(string.Empty, variableStorage.PolicyTokenCount).ToArray();
                ExpressionHandler.SetPolicyParameters(policyValues);
                var nowEffect = GetEffect(ExpressionHandler.Invoke(variableStorage.ExpressionString, requestValues));
                finalResult = _effector.MergeEffects(variableStorage.Effect, new[] { nowEffect }, null, out hitPolicyIndex);
            }

            if (variableStorage.Explain && hitPolicyIndex is not -1)
            {
                explains.Add(variableStorage.PolicyList[hitPolicyIndex]);
            }

#if !NET45
            if (variableStorage.Explain)
            {
                Logger?.LogEnforceResult(requestValues, finalResult, explains);
            }
            else
            {
                Logger?.LogEnforceResult(requestValues, finalResult);
            }
#endif
            return finalResult;
        }

        private bool ProcessChainEffector(EnforcerVariableStorage enforcerVariables,
            bool finalResult = false,
            IReadOnlyList<object> requestValues = null,
            ICollection<IEnumerable<string>> explains = null,
            int maxPriority = int.MaxValue)
        {
            IChainEffector chainEffector = _effector as IChainEffector;
            bool isChainEffector = chainEffector is not null;
            if (isChainEffector)
            {
                chainEffector.StartChain(enforcerVariables.Effect);

                if (enforcerVariables.PolicyCount is not 0)
                {
                    foreach (var policyValues in enforcerVariables.PolicyList
                                                .Where(t=> (maxPriority == int.MaxValue || int.Parse(t[0]) <= maxPriority)))
                    {
                        if (enforcerVariables.PolicyTokenCount != policyValues.Count)
                        {
                            throw new ArgumentException($"Invalid policy size: expected {enforcerVariables.PolicyTokenCount}, got {policyValues.Count}.");
                        }

                        ExpressionHandler.SetPolicyParameters(policyValues);

                        bool expressionResult;

                        if (enforcerVariables.HasEval)
                        {
                            string expressionStringWithRule = RewriteEval(enforcerVariables.ExpressionString, ExpressionHandler.PolicyTokens, policyValues);
                            expressionResult = ExpressionHandler.Invoke(expressionStringWithRule, requestValues);
                        }
                        else
                        {
                            expressionResult = ExpressionHandler.Invoke(enforcerVariables.ExpressionString, requestValues);
                        }

                        var nowEffect = GetEffect(expressionResult);

                        //here
                        if (enforcerVariables.Effect.Equals(PermConstants.PolicyEffect.PriorityDenyOverride)
                                && nowEffect == Effect.Effect.Allow
                                && maxPriority == int.MaxValue)
                        {
                            return ProcessChainEffector(enforcerVariables, finalResult, requestValues, explains, int.Parse(policyValues[0]));
                        }

                        if (nowEffect is not Effect.Effect.Indeterminate && ExpressionHandler.Parameters.TryGetValue("p_eft", out Parameter parameter))
                        {
                            string policyEffect = parameter.Value as string;
                            nowEffect = policyEffect switch
                            {
                                "allow" => Effect.Effect.Allow,
                                "deny" => Effect.Effect.Deny,
                                _ => Effect.Effect.Indeterminate
                            };
                        }

                        bool chainResult = chainEffector.TryChain(nowEffect);

                        if (enforcerVariables.Explain && chainEffector.HitPolicy)
                        {
                            explains.Add(policyValues);
                        }

                        if (chainResult is false || chainEffector.CanChain is false)
                        {
                            break;
                        }
                    }

                    finalResult = chainEffector.Result;
                }
                else
                {
                    if (enforcerVariables.HasEval)
                    {
                        throw new ArgumentException("Please make sure rule exists in policy when using eval() in matcher");
                    }

                    IReadOnlyList<string> policyValues = Enumerable.Repeat(string.Empty, enforcerVariables.PolicyTokenCount).ToArray();
                    ExpressionHandler.SetPolicyParameters(policyValues);
                    var nowEffect = GetEffect(ExpressionHandler.Invoke(enforcerVariables.ExpressionString, requestValues));

                    if (chainEffector.TryChain(nowEffect))
                    {
                        finalResult = chainEffector.Result;
                    }

                    if (enforcerVariables.Explain && chainEffector.HitPolicy)
                    {
                        explains.Add(policyValues);
                    }
                }

#if !NET45
                if (enforcerVariables.Explain)
                {
                    Logger?.LogEnforceResult(requestValues, finalResult, explains);
                }
                else
                {
                    Logger?.LogEnforceResult(requestValues, finalResult);
                }
#endif
                return finalResult;
            }
            return finalResult;
        }

        private bool InternalEnforceFiltered(IReadOnlyList<object> requestValues, int maxRank, string matcher = null, ICollection<IEnumerable<string>> explains = null)
        {
            bool explain = explains is not null;
            string effect = model.Model[PermConstants.Section.PolicyEffectSection][PermConstants.DefaultPolicyEffectType].Value;
            var policyList = model.Model[PermConstants.Section.PolicySection][PermConstants.DefaultPolicyType].Policy
                .Where(t => int.Parse(t[0]) <= maxRank).ToList();

            int policyCount = model.Model[PermConstants.Section.PolicySection][PermConstants.DefaultPolicyType].Policy.Count;

            string expressionString = matcher is not null
                ? Utility.EscapeAssertion(matcher)
                : model.Model[PermConstants.Section.MatcherSection][PermConstants.DefaultMatcherType].Value;

            int requestTokenCount = ExpressionHandler.RequestTokens.Count;
            if (requestTokenCount != requestValues.Count)
            {
                throw new ArgumentException($"Invalid request size: expected {requestTokenCount}, got {requestValues.Count}.");
            }
            int policyTokenCount = ExpressionHandler.PolicyTokens.Count;

            ExpressionHandler.SetRequestParameters(requestValues);

            bool hasEval = Utility.HasEval(expressionString);

            bool finalResult = false;
            IChainEffector chainEffector = _effector as IChainEffector;
            bool isChainEffector = chainEffector is not null;
            if (isChainEffector)
            {
                chainEffector.StartChain(effect);

                if (policyCount is not 0)
                {
                    foreach (var policyValues in policyList)
                    {
                        if (policyTokenCount != policyValues.Count)
                        {
                            throw new ArgumentException($"Invalid policy size: expected {policyTokenCount}, got {policyValues.Count}.");
                        }

                        ExpressionHandler.SetPolicyParameters(policyValues);

                        bool expressionResult;

                        if (hasEval)
                        {
                            string expressionStringWithRule = RewriteEval(expressionString, ExpressionHandler.PolicyTokens, policyValues);
                            expressionResult = ExpressionHandler.Invoke(expressionStringWithRule, requestValues);
                        }
                        else
                        {
                            expressionResult = ExpressionHandler.Invoke(expressionString, requestValues);
                        }

                        var nowEffect = GetEffect(expressionResult);

                        if (nowEffect is not Effect.Effect.Indeterminate && ExpressionHandler.Parameters.TryGetValue("p_eft", out Parameter parameter))
                        {
                            string policyEffect = parameter.Value as string;
                            nowEffect = policyEffect switch
                            {
                                "allow" => Effect.Effect.Allow,
                                "deny" => Effect.Effect.Deny,
                                _ => Effect.Effect.Indeterminate
                            };
                        }

                        bool chainResult = chainEffector.TryChain(nowEffect);

                        if (explain && chainEffector.HitPolicy)
                        {
                            explains.Add(policyValues);
                        }

                        if (chainResult is false || chainEffector.CanChain is false)
                        {
                            break;
                        }
                    }

                    finalResult = chainEffector.Result;
                }
                else
                {
                    if (hasEval)
                    {
                        throw new ArgumentException("Please make sure rule exists in policy when using eval() in matcher");
                    }

                    IReadOnlyList<string> policyValues = Enumerable.Repeat(string.Empty, policyTokenCount).ToArray();
                    ExpressionHandler.SetPolicyParameters(policyValues);
                    var nowEffect = GetEffect(ExpressionHandler.Invoke(expressionString, requestValues));

                    if (chainEffector.TryChain(nowEffect))
                    {
                        finalResult = chainEffector.Result;
                    }

                    if (explain && chainEffector.HitPolicy)
                    {
                        explains.Add(policyValues);
                    }
                }

                return finalResult;
            }

            int hitPolicyIndex;
            if (policyCount != 0)
            {
                Effect.Effect[] policyEffects = new Effect.Effect[policyCount];

                for (int i = 0; i < policyCount; i++)
                {
                    IReadOnlyList<string> policyValues = policyList[i];

                    if (policyTokenCount != policyValues.Count)
                    {
                        throw new ArgumentException($"Invalid policy size: expected {policyTokenCount}, got {policyValues.Count}.");
                    }

                    ExpressionHandler.SetPolicyParameters(policyValues);

                    bool expressionResult;

                    if (hasEval)
                    {
                        string expressionStringWithRule = RewriteEval(expressionString, ExpressionHandler.PolicyTokens, policyValues);
                        expressionResult = ExpressionHandler.Invoke(expressionStringWithRule, requestValues);
                    }
                    else
                    {
                        expressionResult = ExpressionHandler.Invoke(expressionString, requestValues);
                    }

                    var nowEffect = GetEffect(expressionResult);

                    if (nowEffect is Effect.Effect.Indeterminate)
                    {
                        policyEffects[i] = nowEffect;
                        continue;
                    }

                    if (ExpressionHandler.Parameters.TryGetValue("p_eft", out Parameter parameter))
                    {
                        string policyEffect = parameter.Value as string;
                        nowEffect = policyEffect switch
                        {
                            "allow" => Effect.Effect.Allow,
                            "deny" => Effect.Effect.Deny,
                            _ => Effect.Effect.Indeterminate
                        };
                    }

                    policyEffects[i] = nowEffect;

                    if (effect.Equals(PermConstants.PolicyEffect.Priority))
                    {
                        break;
                    }
                }

                finalResult = _effector.MergeEffects(effect, policyEffects, null, out hitPolicyIndex);
            }
            else
            {
                if (hasEval)
                {
                    throw new ArgumentException("Please make sure rule exists in policy when using eval() in matcher");
                }

                IReadOnlyList<string> policyValues = Enumerable.Repeat(string.Empty, policyTokenCount).ToArray();
                ExpressionHandler.SetPolicyParameters(policyValues);
                var nowEffect = GetEffect(ExpressionHandler.Invoke(expressionString, requestValues));
                finalResult = _effector.MergeEffects(effect, new[] { nowEffect }, null, out hitPolicyIndex);
            }

            if (explain && hitPolicyIndex is not -1)
            {
                explains.Add(policyList[hitPolicyIndex]);
            }

#if !NET45
            if (explain)
            {
                Logger?.LogEnforceResult(requestValues, finalResult, explains);
            }
            else
            {
                Logger?.LogEnforceResult(requestValues, finalResult);
            }
#endif
            return finalResult;
        }

        private class EnforcerVariableStorage
        {
            private EnforcerVariableStorage() { }
            private EnforcerVariableStorage(
                string effect,
                bool explain,
                string expressionString,
                bool hasEval,
                IList<List<string>> policyList,
                int policyCount,
                int policyTokenCount,
                int requestTokenCount)
            {
                this.Effect = effect;
                this.Explain = explain;
                this.ExpressionString = expressionString;
                this.HasEval = hasEval;
                this.PolicyList = policyList;
                this.PolicyCount = policyCount;
                this.PolicyTokenCount = policyTokenCount;
                this.RequestTokenCount = requestTokenCount;
            }

            public static EnforcerVariableStorage Init(
                string matcher,
                ICollection<IEnumerable<string>> explains,
                Model.Model model,
                IExpressionHandler ExpressionHandler)
            {
                var explain = explains is not null;
                var effect = model.Model[PermConstants.Section.PolicyEffectSection][PermConstants.DefaultPolicyEffectType].Value;
                var policyList = model.Model[PermConstants.Section.PolicySection][PermConstants.DefaultPolicyType].Policy;
                var policyCount = model.Model[PermConstants.Section.PolicySection][PermConstants.DefaultPolicyType].Policy.Count;

                var expressionString = matcher is not null
                    ? Utility.EscapeAssertion(matcher)
                    : model.Model[PermConstants.Section.MatcherSection][PermConstants.DefaultMatcherType].Value;

                var hasEval = Utility.HasEval(expressionString);

                return new EnforcerVariableStorage(
                    effect,
                    explain,
                    expressionString,
                    hasEval,
                    policyList,
                    policyCount,
                    ExpressionHandler.PolicyTokens.Count,
                    ExpressionHandler.RequestTokens.Count);
            }

            #region Properties
            internal bool Explain { get; }
            internal string Effect { get; }
            internal int PolicyCount { get; }
            internal IList<List<string>> PolicyList { get; }
            internal int PolicyTokenCount { get; }
            internal string ExpressionString { get; }
            internal int RequestTokenCount { get; }
            internal bool HasEval { get; }
            #endregion
        }

        private static Effect.Effect GetEffect(bool expressionResult)
        {
            return expressionResult ? Effect.Effect.Allow : Effect.Effect.Indeterminate;
        }

        private static string RewriteEval(string expressionString, IDictionary<string, int> policyTokens, IReadOnlyList<string> policyValues)
        {
            if (Utility.TryGetEvalRuleNames(expressionString, out IEnumerable<string> ruleNames) is false)
            {
                return expressionString;
            }

            Dictionary<string, string> rules = new();
            foreach (string ruleName in ruleNames)
            {
                if (policyTokens.TryGetValue(ruleName, out int ruleIndex) is false)
                {
                    throw new ArgumentException("Please make sure rule exists in policy when using eval() in matcher");
                }
                rules[ruleName] = Utility.EscapeAssertion(policyValues[ruleIndex]);
            }

            expressionString = Utility.ReplaceEval(expressionString, rules);
            return expressionString;
        }
    }
}
