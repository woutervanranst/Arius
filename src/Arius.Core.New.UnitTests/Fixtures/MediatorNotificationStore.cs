using MediatR;

namespace Arius.Core.New.UnitTests.Fixtures;

public class MediatorNotificationStore
{
    public List<INotification> notifications = new();

    public void AddNotification(INotification n)
    {
        lock (notifications)
        {
            notifications.Add(n);
        }
    }

    public IEnumerable<INotification> Notifications => notifications.AsReadOnly();
}

public class GenericMediatrNotificationHandler<T>
    : INotificationHandler<T> where T : INotification
{
    private readonly MediatorNotificationStore ns;

    public GenericMediatrNotificationHandler(MediatorNotificationStore ns)
    {
        this.ns = ns;
    }
    public async Task Handle(T notification, CancellationToken cancellationToken)
    {
        ns.AddNotification(notification);
        await Task.CompletedTask;
    }
}