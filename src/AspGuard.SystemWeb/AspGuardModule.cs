using System.Web;

namespace AspGuard.SystemWeb;

/// <summary>Wires <see cref="RequestGuard"/> into the ASP.NET 4.x pipeline. Registered by <see cref="AspGuardSystemWeb.Start"/>.</summary>
internal sealed class AspGuardModule : IHttpModule
{
    public void Init(HttpApplication context)
    {
        context.BeginRequest += OnBeginRequest;
        context.PreSendRequestHeaders += OnPreSendRequestHeaders;
        context.EndRequest += OnEndRequest;
    }

    public void Dispose()
    {
    }

    private static void OnBeginRequest(object sender, EventArgs e)
    {
        var application = (HttpApplication)sender;
        if (RequestGuard.Current.OnBeginRequest(new HttpContextWrapper(application.Context)))
        {
            application.CompleteRequest();
        }
    }

    private static void OnPreSendRequestHeaders(object sender, EventArgs e) =>
        RequestGuard.Current.OnPreSendRequestHeaders(new HttpContextWrapper(((HttpApplication)sender).Context), HttpRuntime.UsingIntegratedPipeline);

    private static void OnEndRequest(object sender, EventArgs e) =>
        RequestGuard.Current.OnEndRequest(new HttpContextWrapper(((HttpApplication)sender).Context));
}
