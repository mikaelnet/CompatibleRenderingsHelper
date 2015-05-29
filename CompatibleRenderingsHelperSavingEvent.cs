using System;
using System.Collections.Generic;
using System.Linq;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;

namespace Stendahls.Sc.CompatibleRenderingsHelper
{
    /// <summary>
    /// This event handler typically triggers on saving an item
    /// It checks if the saving item has a compatible renderings field
    /// and updates the referenced rendering items with the equivalent
    /// data.
    /// </summary>
    public class CompatibleRenderingsHelperSavingEvent
    {
        public string Database { get; set; }
        public static readonly ID CompatibleRenderingsFieldID = new ID("{E441ABE7-2CA3-4640-AE26-3789967925D7}");

        public void OnItemSaving(object sender, EventArgs args)
        {
            // Exit this method as early as possible if no work is needed.
            var eventArgs = args as Sitecore.Events.SitecoreEventArgs;
            if(eventArgs == null || eventArgs.Parameters == null)
                return;

            // Get the item we're updating
            var updatedItem = eventArgs.Parameters[0] as Item;
            if (updatedItem == null)
                return;

            // Make sure we're in the configured database
            if (Database != null && !String.Equals(updatedItem.Database.Name, Database, StringComparison.InvariantCultureIgnoreCase))
                return; // Wrong database

            // Make sure we're editing an item that implements the LastModified template
            if (!ItemUtil.IsRenderingItem(updatedItem))
                return; // Not a rendering item

            // Load and parse the existing compatible renderings. 
            var existingItem = updatedItem.Database.GetItem(updatedItem.ID, updatedItem.Language, updatedItem.Version);
            if (existingItem == null && string.IsNullOrWhiteSpace(updatedItem[CompatibleRenderingsFieldID]))
                return; // New item with no compatible rendering
            
            if (existingItem != null && 
                string.Equals(existingItem[CompatibleRenderingsFieldID], updatedItem[CompatibleRenderingsFieldID]))
                return; // Existing item, but compatible rendering not changed.

            try
            {
                var newListOfRenderings = new List<ID>();
                var renderingsCache = new Dictionary<ID, Item>();

                // Have a list of the new compatible renderings. Keep its order but remove duplicates
                // (The UI typically prevents duplicates anyway though)
                foreach (ID id in ID.ParseArray(updatedItem[CompatibleRenderingsFieldID]))
                {
                    if (!newListOfRenderings.Contains(id))
                        newListOfRenderings.Add(id);
                }

                // Ensure all renderings exists and are of right type.
                bool foundNonRenderingItem = false;
                for (int index = newListOfRenderings.Count - 1; index >= 0; index--)
                {
                    ID id = newListOfRenderings[index];
                    var item = updatedItem.Database.GetItem(id);
                    if (item == null || !ItemUtil.IsRenderingItem(item))
                    {
                        newListOfRenderings.RemoveAt(index);
                        foundNonRenderingItem = true;
                    }
                    else
                    {
                        renderingsCache.Add(id, item);
                    }
                }

                // The current item is only modified if there are corrections that needs to be made.
                if (foundNonRenderingItem)
                {
                    updatedItem[CompatibleRenderingsFieldID] = ID.ArrayToString(newListOfRenderings.ToArray());
                }

                // If the current item is included in the compatible renderings list, it is
                // assumed that the list should be the same on all renderings. If not, the
                // list should be just all other renderings except itself.
                bool includeCurrent = newListOfRenderings.Contains(updatedItem.ID);
                if (!includeCurrent)
                {
                    // This will put the current rendering at the top on all others
                    // and after that keep the current sort order. Think of this as the primary layout.
                    newListOfRenderings.Insert(0, updatedItem.ID);
                    renderingsCache[updatedItem.ID] = updatedItem;
                }

                // Loop through all referenced items and update their compatible renderings field
                // (skip the current one - it's already set)
                foreach (ID id in newListOfRenderings.Where(i => i != updatedItem.ID))
                {
                    var item = renderingsCache[id];
                    var ids = ID.ArrayToString(includeCurrent ? 
                        newListOfRenderings.ToArray() : 
                        newListOfRenderings.Where(i => i != id).ToArray());

                    SetCompatibleRenderings(item, ids);
                }

                // Remove compatible renderings from items that was removed during this event
                if (existingItem != null && !string.IsNullOrWhiteSpace(existingItem[CompatibleRenderingsFieldID]))
                {
                    foreach (ID id in ID.ParseArray(existingItem[CompatibleRenderingsFieldID]).Except(newListOfRenderings))
                    {
                        var item = updatedItem.Database.GetItem(id);
                        if (item != null && ItemUtil.IsRenderingItem(item))
                        {
                            SetCompatibleRenderings(item, null);
                        }
                    }
                }
                
            }
            catch (Exception ex)
            {
                Log.Error("Error updating compatible renderings", ex, this);
            }
        }

        private void SetCompatibleRenderings(Item item, string value)
        {
            Log.Info(string.Format("Updating Compatible Renderings field on {0} (from {1} to {2})",
                item.ID, item.Fields[CompatibleRenderingsFieldID].Value, value), this);

            // It's very important that this is a "silent" update. It'll recurse otherwise.
            item.Editing.BeginEdit();
            item[CompatibleRenderingsFieldID] = value;
            item.Editing.EndEdit(true);

            // Silent updates may need cache clearing
            item.Database.Caches.ItemCache.RemoveItem(item.ID);
            item.Database.Caches.DataCache.RemoveItemInformation(item.ID);
        }
    }
}