using Projekat3_Zadatak18.Dodaci;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace Projekat3_Zadatak18
{
    internal class Server
    {
        private readonly HttpListener _listener;
        private int _brojAktivnihZahteva = 0;
        private readonly ManualResetEvent _sviZahteviGotovi = new ManualResetEvent(false);
        private readonly CocktailHandler _cocktailHandler;

        private readonly IObservable<HttpListenerContext> _requestStream;
        private readonly CancellationTokenSource _cts = new();
        private IDisposable? _subscription;

        public Server(string urlPrefix)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(urlPrefix);
            _cocktailHandler = new CocktailHandler();

            var zaustaviSe = Observable
                .FromEvent(
                    a => _cts.Token.Register(a),
                    a => { }
                );

            _requestStream = Observable
                    .Defer(() => Observable.FromAsync(_listener.GetContextAsync))
                    .Repeat()
                    .TakeUntil(zaustaviSe)
                    .Where(ctx => !string.Equals(ctx.Request.Url.AbsolutePath.TrimStart('/'),"favicon.ico",StringComparison.OrdinalIgnoreCase))
                    .SelectMany(context =>
                        Observable.FromAsync(async () =>
                        {
                            Interlocked.Increment(ref _brojAktivnihZahteva);
                            try
                            {
                                await ObradiZahtevAsync(context, Thread.CurrentThread.ManagedThreadId);
                                return context;
                            }
                            finally
                            {
                                if (Interlocked.Decrement(ref _brojAktivnihZahteva) == 0)
                                    _sviZahteviGotovi.Set();
                            }
                        })
                        .SubscribeOn(TaskPoolScheduler.Default)
                    );
        }

        public void Start()
        {
            _listener.Start();
            ConsoleInfo.Server("Server je startovan.");

            _subscription = _requestStream.Subscribe(
               _ => { }, //Obradjeno u SelectMany
               ex => ConsoleInfo.Greska("Greška u obradi zahteva", ex)
            );
        }

        public void Stop()
        {
            ConsoleInfo.Server("Server ce biti zaustavljen cim se zavrse svi aktivni zahtevi...");
            _cts.Cancel();
            if (_brojAktivnihZahteva == 0)
                _sviZahteviGotovi.Set();
            _sviZahteviGotovi.WaitOne();
            _listener.Stop();
            _subscription?.Dispose();
            ConsoleInfo.Server("Server zaustavljen.");
        }

        private async Task ObradiZahtevAsync(HttpListenerContext context,int id)
        {
            string putanja = context.Request.Url.AbsolutePath.TrimStart('/');       
            if (putanja == "favicon.ico")
            {
                await PosaljiOdgovorAsync(context, 204, "No Content");
                return;
            }
            string query = context.Request.Url.Query;
            string? param = context.Request.QueryString["letter"];
            ConsoleInfo.Log("Zahtev primljen",putanja+query);
            ConsoleInfo.Start($"Nit {id} pocinje obradu zahteva", putanja + query);
            if (!(putanja.StartsWith("ingredients", StringComparison.OrdinalIgnoreCase)))
            {
                ConsoleInfo.Greska("Zahtev mora poceti sa ingredients.");
                await PosaljiOdgovorAsync(context, 400, "Bad Request.");
                return;
            }
            /*if (string.IsNullOrWhiteSpace(putanja))
            {
                ConsoleInfo.Greska("Lose postavljen zahtev.");
                await PosaljiOdgovorAsync(context, 400, "Bad Request.");
                return;
            }*/
            if (context.Request.HttpMethod.ToLower() != "get")
            {
                ConsoleInfo.Greska("Losa metoda zahteva (metoda mora biti GET).");
                await PosaljiOdgovorAsync(context, 400, "Bad Request.");
                return;
            }
            if (string.IsNullOrWhiteSpace(query) ||!query.Contains("letter="))
            {
                ConsoleInfo.Greska("Lose postavljen upit.");
                await PosaljiOdgovorAsync(context, 400, "Bad Request.");                
                return;
            }
            if (string.IsNullOrWhiteSpace(param) || param.Length != 1)
            {
                ConsoleInfo.Greska("Parametar upita mora biti tacno jedno slovo.");
                await PosaljiOdgovorAsync(context, 400, "Bad Request.");                
                return;
            }
            char pocetnoSlovo = param[0];
            if (!Ekstenzije.ProveraOpsega(pocetnoSlovo))
            {
                ConsoleInfo.Greska("Parametar upita mora biti slovo.");
                await PosaljiOdgovorAsync(context, 400, "Bad Request.");                
                return;
            }
            try
            {
                await _cocktailHandler.ObradaZahtevaAsync(context,pocetnoSlovo);
                ConsoleInfo.Info("Isporuceni sastojci za pica sa pocetnim slovom",pocetnoSlovo);
                ConsoleInfo.End($"Nit {id} zavrsava obradu zahteva", putanja + query);
            }
            catch (Exception ex)
            {
                ConsoleInfo.Greska("Doslo je do greske", ex);
                await PosaljiOdgovorAsync(context, 500, "Internal Server Error.");
            }
        }

        private async Task PosaljiOdgovorAsync(HttpListenerContext ctx, int statusCode, string message)
        {
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "text/plain";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            ctx.Response.ContentLength64 = buffer.Length;
            await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();
        }
    }
}
