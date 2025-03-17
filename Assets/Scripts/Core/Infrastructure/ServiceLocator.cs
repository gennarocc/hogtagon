using UnityEngine;
using System;
using System.Collections.Generic;

namespace Hogtagon.Core.Infrastructure
{
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> services = new Dictionary<Type, object>();
        private static readonly Dictionary<Type, List<Action>> pendingCallbacks = new Dictionary<Type, List<Action>>();

        public static void RegisterService<T>(T service) where T : class
        {
            Type type = typeof(T);

            if (services.ContainsKey(type))
            {
                Debug.LogWarning($"Service of type {type.Name} is already registered. Overwriting...");
            }

            services[type] = service;

            // Execute any pending callbacks
            if (pendingCallbacks.TryGetValue(type, out var callbacks))
            {
                foreach (var callback in callbacks)
                {
                    callback?.Invoke();
                }
                pendingCallbacks.Remove(type);
            }
        }

        public static T GetService<T>() where T : class
        {
            Type type = typeof(T);

            if (services.TryGetValue(type, out object service))
            {
                return service as T;
            }

            Debug.LogWarning($"Service of type {type.Name} not found.");
            return null;
        }

        public static void WaitForService<T>(Action callback) where T : class
        {
            Type type = typeof(T);

            if (services.ContainsKey(type))
            {
                callback?.Invoke();
                return;
            }

            if (!pendingCallbacks.ContainsKey(type))
            {
                pendingCallbacks[type] = new List<Action>();
            }

            pendingCallbacks[type].Add(callback);
        }

        public static bool HasService<T>() where T : class
        {
            return services.ContainsKey(typeof(T));
        }

        public static void UnregisterService<T>() where T : class
        {
            Type type = typeof(T);

            if (services.ContainsKey(type))
            {
                services.Remove(type);
            }
        }

        public static void ClearAllServices()
        {
            services.Clear();
            pendingCallbacks.Clear();
        }
    }

    // Example usage:
    /*
    // Service interface
    public interface IGameService
    {
        void DoSomething();
    }

    // Service implementation
    public class GameService : IGameService
    {
        public void DoSomething() { }
    }

    // Register service
    ServiceLocator.RegisterService<IGameService>(new GameService());

    // Get service
    var gameService = ServiceLocator.GetService<IGameService>();

    // Wait for service
    ServiceLocator.WaitForService<IGameService>(() => {
        var service = ServiceLocator.GetService<IGameService>();
        service.DoSomething();
    });
    */
} 