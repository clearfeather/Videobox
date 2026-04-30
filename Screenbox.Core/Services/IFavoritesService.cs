#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Screenbox.Core.ViewModels;

namespace Screenbox.Core.Services;

public interface IFavoritesService
{
    Task<IReadOnlyList<MediaViewModel>> LoadFavoritesAsync();

    Task SaveFavoritesAsync(IReadOnlyList<MediaViewModel> favorites);
}
