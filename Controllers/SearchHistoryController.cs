using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTravelAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace TravelApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SearchHistoryController : ControllerBase
    {
        private readonly DataContext _context;

        public SearchHistoryController(DataContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<SearchHistory>>> GetSearchHistory()
        {
            return await _context.SearchHistory.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<SearchHistory>> GetSearchHistory(int id)
        {
            var search = await _context.SearchHistory.FindAsync(id);

            if (search == null)
                return NotFound();

            return search;
        }

        [HttpPost]
        public async Task<ActionResult<SearchHistory>> PostSearchHistory(SearchHistory search)
        {
            _context.SearchHistory.Add(search);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSearchHistory), new { id = search.SearchId }, search);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutSearchHistory(int id, SearchHistory search)
        {
            if (id != search.SearchId)
                return BadRequest();

            _context.Entry(search).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SearchHistoryExists(id))
                    return NotFound();
                else
                    throw;
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSearchHistory(int id)
        {
            var search = await _context.SearchHistory.FindAsync(id);
            if (search == null)
                return NotFound();

            _context.SearchHistory.Remove(search);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool SearchHistoryExists(int id)
        {
            return _context.SearchHistory.Any(e => e.SearchId == id);
        }
    }
}
