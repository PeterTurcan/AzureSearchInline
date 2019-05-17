using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using AzureSearchAjax.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;

using System.Collections.Generic;
using System.Linq;

namespace AzureSearchAjax.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public JsonResult AjaxMethod(string term)
        {
            var m = new SearchData
            {
                searchText = "text is " + term,
                resultCount = 23,
            };
            return Json(m);
        }


        [HttpPost]
        public async Task<ActionResult> Index(SearchData model)
        {
            try
            {
                // A new search so set the current page to zero and ensure a valid search string.
                TempData["page"] = (object)0;
                TempData["leftMostPage"] = (object)0;

                if (model.searchText == null)
                {
                    model.searchText = "";
                }
                TempData["searchfor"] = model.searchText;

                await runQueryAsync(model, 0, 0);
            }

            catch
            {
                return View("Error", new ErrorViewModel { RequestId = "1" });
            }
            return View(model);
        }
        public async Task<ActionResult> Next(SearchData model)
        {
            try
            {
                // Increment the current page.
                int page = 1 + (int)TempData["page"];
                TempData["page"] = (object)page;

                // Recover the search text and search for the data for the new page.
                model.searchText = TempData["searchfor"].ToString();
                await runQueryAsync2(model, page);

                // Ensure search string is stored for next call, as TempData only stored for one call.
                TempData["searchfor"] = model.searchText;
            }

            catch
            {
                return View("Error", new ErrorViewModel { RequestId = "2" });
            }

            List<string> hotels = new List<string>();
            for (int n = 0; n < model.hotels.Count; n++)
            {
                hotels.Add(model.getHotel(n).HotelName);
                hotels.Add(model.getHotelDescription(n));
            }
            return new JsonResult((object)hotels);
        }
        public async Task<ActionResult> Page(SearchData model)
        {
            try
            {
                int page;

                switch (model.paging)
                {
                    case "prev":
                        page = (int)TempData["page"] - 1;
                        break;

                    case "next":
                        page = (int)TempData["page"] + 1;
                        break;

                    default:
                        page = int.Parse(model.paging);
                        break;
                }

                // Recover the leftMostPage
                int leftMostPage = (int)TempData["leftMostPage"];

                // Recover the search text and search for the data for the new page.
                model.searchText = TempData["searchfor"].ToString();

                await runQueryAsync(model, page, leftMostPage);

                // Ensure Temp data is stored for next call, as TempData only stored for one call.
                TempData["page"] = (object)page;
                TempData["searchfor"] = model.searchText;
                TempData["leftMostPage"] = model.leftMostPage;
            }

            catch
            {
                return View("Error", new ErrorViewModel { RequestId = "2" });
            }
            return View("Index", model);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private static SearchServiceClient CreateSearchServiceClient(IConfigurationRoot configuration)
        {
            string searchServiceName = configuration["SearchServiceName"];
            string queryApiKey = configuration["SearchServiceQueryApiKey"];

            SearchServiceClient serviceClient = new SearchServiceClient(searchServiceName, new SearchCredentials(queryApiKey));
            return serviceClient;
        }

        private static SearchServiceClient _serviceClient;
        private static ISearchIndexClient _indexClient;
        private static IConfigurationBuilder _builder;
        private static IConfigurationRoot _configuration;

        private async Task<ActionResult> runQueryAsync(SearchData model, int page, int leftMostPage)
        {
            // Use static variables to set up the configuration and Azure service and index clients, for efficiency.
            _builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            _configuration = _builder.Build();

            _serviceClient = CreateSearchServiceClient(_configuration);
            _indexClient = _serviceClient.Indexes.GetClient("hotels");

            SearchParameters parameters;
            DocumentSearchResult<Hotel> results;

            parameters =
               new SearchParameters()
               {
                   // Add this so that "King's Palace Hotel" does not match with all hotels!
                   SearchMode = SearchMode.All,
                   
                   // Enter Hotel property names into this list so only these values will be returned.
                   // If Select is empty, all values will be returned, which can be inefficient.
                   Select = new[] { "HotelName", "Description", "Tags", "Rooms" }
               };

            // For efficiency, the search call should ideally be asynchronous, so we use the
            // SearchAsync call rather than the Search call.
            results = await _indexClient.Documents.SearchAsync<Hotel>(model.searchText, parameters);

            if (results.Results == null)
            {
                model.resultCount = 0;
            }
            else
            {
                // Record the total number of results.
                model.resultCount = (int)results.Results.Count;

                // Calcuate the range of current page results.
                int start = page * GlobalVariables.ResultsPerPage;
                int end = Math.Min(model.resultCount, (page + 1) * GlobalVariables.ResultsPerPage);

                for (int i = start; i < end; i++)
                {
                    // Check for hotels with no room data provided.
                    if (results.Results[i].Document.Rooms.Length > 0)
                    {
                        // Add a hotel with sample room data (an example of a "complex type").
                        model.AddHotel(results.Results[i].Document.HotelName,
                             results.Results[i].Document.Description,
                             (double)results.Results[i].Document.Rooms[0].BaseRate,
                             results.Results[i].Document.Rooms[0].BedOptions,
                             results.Results[i].Document.Tags);
                    }
                    else
                    {
                        // Add a hotel with no sample room data.
                        model.AddHotel(results.Results[i].Document.HotelName,
                            results.Results[i].Document.Description,
                            0d,
                            "No room data provided",
                            results.Results[i].Document.Tags);
                    }
                }

                // Calcuate the page count.
                model.pageCount = (model.resultCount + GlobalVariables.ResultsPerPage - 1) / GlobalVariables.ResultsPerPage;
                model.currentPage = page;

                if (page == 0)
                {
                    leftMostPage = 0;
                }
                else
                   if (page <= leftMostPage)
                {
                    leftMostPage = Math.Max(page - GlobalVariables.PageRangeDelta, 0);
                }
                else
                if (page >= leftMostPage + GlobalVariables.MaxPageRange - 1)
                {
                    leftMostPage = Math.Min(leftMostPage + GlobalVariables.PageRangeDelta, model.pageCount - GlobalVariables.MaxPageRange);
                }
                model.leftMostPage = leftMostPage;
                model.pageRange = Math.Min(model.pageCount - leftMostPage, GlobalVariables.MaxPageRange);
            }
            return View("Index", model);
        }

        private async Task<ActionResult> runQueryAsync2(SearchData model, int page)
        {
            // Use static variables to set up the configuration and Azure service and index clients, for efficiency.
            _builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            _configuration = _builder.Build();

            _serviceClient = CreateSearchServiceClient(_configuration);
            _indexClient = _serviceClient.Indexes.GetClient("hotels");

            SearchParameters parameters;
            DocumentSearchResult<Hotel> results;

            parameters =
               new SearchParameters()
               {
                   // Add this so that "King's Palace Hotel" does not match with all hotels!
                   SearchMode = SearchMode.All,

                   // Enter Hotel property names into this list so only these values will be returned.
                   // If Select is empty, all values will be returned, which can be inefficient.
                   Select = new[] { "HotelName", "Description", "Tags", "Rooms" }
               };

            // For efficiency, the search call should ideally be asynchronous, so we use the
            // SearchAsync call rather than the Search call.
            results = await _indexClient.Documents.SearchAsync<Hotel>(model.searchText, parameters);

            if (results.Results == null)
            {
                model.resultCount = 0;
            }
            else
            {
                // Record the total number of results.
                model.resultCount = (int)results.Results.Count;

                // Calcuate the range of current page results.
                int start = page * GlobalVariables.ResultsPerPage;
                int end = Math.Min(model.resultCount, (page + 1) * GlobalVariables.ResultsPerPage);

                for (int i = start; i < end; i++)
                {
                    // Check for hotels with no room data provided.
                    if (results.Results[i].Document.Rooms.Length > 0)
                    {
                        // Add a hotel with sample room data (an example of a "complex type").
                        model.AddHotel(results.Results[i].Document.HotelName,
                             results.Results[i].Document.Description,
                             (double)results.Results[i].Document.Rooms[0].BaseRate,
                             results.Results[i].Document.Rooms[0].BedOptions,
                             results.Results[i].Document.Tags);
                    }
                    else
                    {
                        // Add a hotel with no sample room data.
                        model.AddHotel(results.Results[i].Document.HotelName,
                            results.Results[i].Document.Description,
                            0d,
                            "No room data provided",
                            results.Results[i].Document.Tags);
                    }
                }

                // Calcuate the page count.
                model.pageCount = (model.resultCount + GlobalVariables.ResultsPerPage - 1) / GlobalVariables.ResultsPerPage;
                model.currentPage = page;

            }
            JsonResult jr = new JsonResult((object)model);

            return jr;
        }

        public async Task<ActionResult> Suggest(bool highlights, bool fuzzy, string term)
        {
            // Use static variables to set up the configuration and Azure service and index clients, for efficiency.
            _builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            _configuration = _builder.Build();

            _serviceClient = CreateSearchServiceClient(_configuration);
            _indexClient = _serviceClient.Indexes.GetClient("hotels");

            // Call suggest API and return results
            SuggestParameters sp = new SuggestParameters()
            {
                UseFuzzyMatching = fuzzy,
                Top = 8,
            };

            if (highlights)
            {
                sp.HighlightPreTag = "<b>";
                sp.HighlightPostTag = "</b>";
            }

            // Only one suggester can be specified per index. The name of the suggester is set when the suggester is specified by other API calls.
            // The suggester for the hotel database is called "sg" and simply searches the hotel name.
            DocumentSuggestResult<Hotel> suggestResult = await _indexClient.Documents.SuggestAsync<Hotel>(term, "sg", sp);

            // Convert the suggest query results to a list that can be displayed in the client.
            List<string> suggestions = suggestResult.Results.Select(x => x.Text).ToList();
            return new JsonResult((object)suggestions);
        }

        public async Task<ActionResult> AutoComplete(string term)
        {
            // Use static variables to set up the configuration and Azure service and index clients, for efficiency.
            _builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            _configuration = _builder.Build();

            _serviceClient = CreateSearchServiceClient(_configuration);
            _indexClient = _serviceClient.Indexes.GetClient("hotels");

            //Call autocomplete API and return results
            AutocompleteParameters ap = new AutocompleteParameters()
            {
                AutocompleteMode = AutocompleteMode.OneTermWithContext,
                UseFuzzyMatching = false,
                Top = 5
            };
            AutocompleteResult autocompleteResult = await _indexClient.Documents.AutocompleteAsync(term, "sg", ap);

            // Convert the autocompleteResult results to a list that can be displayed in the client.
            List<string> autocomplete = autocompleteResult.Results.Select(x => x.Text).ToList();

            return new JsonResult((object)autocomplete);
        }

        public async Task<ActionResult> Facets()
        {
            // Use static variables to set up the configuration and Azure service and index clients, for efficiency.
            _builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            _configuration = _builder.Build();

            _serviceClient = CreateSearchServiceClient(_configuration);
            _indexClient = _serviceClient.Indexes.GetClient("hotels");

            // Call facets API and return results
            SearchParameters sp = new SearchParameters()
            {
                // Search all Tags, but limit the total number to 100, and add up to 20 categories.
                // Field names specified here must be marked as "IsFacetable" in the model, or the search call will throw an exception.
                Facets = new List<string> { "Tags,count:100", "Category,count:20" },
                Top = 0
            };

            DocumentSearchResult<Hotel> searchResult = await _indexClient.Documents.SearchAsync<Hotel>("*", sp);

            // Convert the results to a list that can be displayed in the client.
            List<string> facets = searchResult.Facets["Tags"].Select(x => x.Value.ToString()).ToList();
            List<string> categories = searchResult.Facets["Category"].Select(x => x.Value.ToString()).ToList();

            // Combine and return the lists.
            facets.AddRange(categories);
            return new JsonResult((object)facets);
        }
    }
}


/* Issues:
 * 1. is there a compact List<string> method?  .ToList etc.  YES - using Linq
 * 2. what does the "sg" method do, are there others?  - NO
 * 3. highlights coming out as <b>text</b> - done
 * 
 * 4. all async functions etc need Try catch blocks - NO - they are OK
 * 
 * 5. getJSON not working in either scenario (autocomplete, facets) - calls controller, but nothing displayed - FACETS working
 * 
 * 
 * 6.   inline autocomplete is not working
 */

