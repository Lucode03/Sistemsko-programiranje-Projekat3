using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Projekat3_Zadatak18.Dodaci;

namespace Projekat3_Zadatak18
{
    internal class CocktailHandler
    {
        private static readonly HttpClient http = new HttpClient
        {
            BaseAddress = new Uri("https://www.thecocktaildb.com/api/json/v1/1/")
        };

        public async Task ObradaZahtevaAsync(HttpListenerContext context,char c)
        {
            var pocetnoSlovo = char.ToLower(c);

            //await Task.Delay(5000);

            await Observable.FromAsync(() => VratiKoktele(pocetnoSlovo))
                .SubscribeOn(TaskPoolScheduler.Default)
                .SelectMany(drinks => drinks.SelectMany(IzvuciSastojke))
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .GroupBy(i => i.ToLower())
                .SelectMany(g =>
                    g.Count().Select(count => new { Ingredient = g.Key, Count = count })
                )
                .ToList()
                .SelectMany(list => Observable.FromAsync(() =>
                    PosaljiOdgovorJsonAsync(context, 200, list)
                ))
                .LastAsync();
        }

        private async Task<Drink[]> VratiKoktele(char pocetnoSlovo)
        {
            var response = await http.GetStringAsync($"search.php?f={pocetnoSlovo}");
            var result = JsonSerializer.Deserialize<RezultatiPretrage>(response);
            return result?.drinks ?? Array.Empty<Drink>();
        }

        private string[] IzvuciSastojke(Drink d)
        {
            return new[]
            {
                d.strIngredient1,
                d.strIngredient2,
                d.strIngredient3, 
                d.strIngredient4,
                d.strIngredient5, 
                d.strIngredient6, 
                d.strIngredient7,
                d.strIngredient8,
                d.strIngredient9,
                d.strIngredient10,
                d.strIngredient11,
                d.strIngredient12,
                d.strIngredient13,
                d.strIngredient14,
                d.strIngredient15
            }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        }

        private async Task PosaljiOdgovorJsonAsync(HttpListenerContext context, int statusCode, object data)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(data);
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(json);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
    }
}
