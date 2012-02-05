﻿using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Text;

namespace Depressurizer {
    public partial class FormMain : Form {
        #region Constants
        // Special names shown in the category list
        const string CAT_ALL_NAME = "<All>";
        const string CAT_FAV_NAME = "<Favorite>";
        const string CAT_UNC_NAME = "<Uncategorized>";

        // Special names shown in the category combobox
        const string COMBO_NEWCAT = "Add new...";
        const string COMBO_NONE = "Remove category";
        #endregion
        #region Fields
        // Stores currently loaded profile
        ProfileData currentProfile;
        // Stores all actual game data
        GameData gameData;

        // Game list sorting state
        int sortColumn = 1;
        int sortDirection = 1;

        // Stores last selected category to minimize game list refreshes
        object lastSelectedCat = null;

        bool unsavedChanges = false;

        DepSettings settings = DepSettings.Instance();

        StringBuilder statusBuilder = new StringBuilder();
        #endregion

        #region Properties
        public bool ProfileLoaded {
            get {
                return currentProfile != null;
            }
        }
        #endregion

        public FormMain() {
            gameData = new GameData();
            InitializeComponent();
            combFavorite.SelectedIndex = 0;
            UpdateGameSorter();
            FillCategoryList();
        }

        public void AddStatus( string s ) {
            statusBuilder.Append( s );
            statusBuilder.Append( ' ' );
        }

        public void ClearStatus() {
            statusBuilder.Clear();
        }

        public void FlushStatus() {
            statusMsg.Text = statusBuilder.ToString();
            statusBuilder.Clear();
        }

        public void BlankStatus() {
            statusMsg.Text = string.Empty;
        }

        public void MakeChange( bool changes ) {
            if( unsavedChanges != changes ) {
                unsavedChanges = changes;
                UpdateTitle();
            } else {
                unsavedChanges = changes;
            }
        }

        #region Manual Operations
        /// <summary>
        /// Loads a Steam configuration file and adds its data to the currently loaded game list. Asks the user to select a file, handles the load, and refreshes the UI.
        /// </summary>
        void ManualImport() {
            if( ProfileLoaded || gameData.Games.Count > 0 ) {
                if( MessageBox.Show( "This action will add the contents of a Steam config file to the currently loaded game list, and will overwrite the category information for any existing games. If you do not want to do this, close the open profile or gamelist first.\nContinue loading file?",
                    "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Information )
                 == DialogResult.No ) {
                    return;
                }
            }

            OpenFileDialog dlg = new OpenFileDialog();
            DialogResult res = dlg.ShowDialog();
            if( res == DialogResult.OK ) {
                Cursor = Cursors.WaitCursor;
                try {
                    int loadedGames = gameData.ImportSteamFile( dlg.FileName );
                    if( loadedGames == 0 ) {
                        MessageBox.Show( "Warning: No game info found in the specified file.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning );
                        AddStatus( "No games found." );
                    } else {
                        MakeChange( true );
                        AddStatus( string.Format( "Imported {0} games.", loadedGames ) );
                        lastSelectedCat = null; // Make sure the game list refreshes
                        FillCategoryList();
                    }
                } catch( ApplicationException e ) {
                    MessageBox.Show( e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error );
                    AddStatus( "Import failed." );
                }
                Cursor = Cursors.Default;
            }
        }

        /// <summary>
        /// Saves a Steam configuration file. Asks the user to select the file to save as.
        /// </summary>
        /// <returns>True if save was completed, false otherwise</returns>
        bool ManualExport() {
            SaveFileDialog dlg = new SaveFileDialog();
            DialogResult res = dlg.ShowDialog();
            if( res == DialogResult.OK ) {
                Cursor = Cursors.WaitCursor;
                try {
                    gameData.SaveSteamFile( dlg.FileName, settings.RemoveExtraEntries );
                    AddStatus( "Data exported." );
                    return true;
                } catch( ApplicationException e ) {
                    MessageBox.Show( e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error );
                    AddStatus( "Export failed." );
                }
                Cursor = Cursors.Default;
            }
            return false;
        }

        /// <summary>
        /// Loads game list information from a Steam profile and adds to currently loaded game list. Asks user for Steam profile name.
        /// </summary>
        public void ManualDownload() {

            if( ProfileLoaded || gameData.Games.Count > 0 ) {
                if( MessageBox.Show( "This action will add the contents of a Steam community game list to the currently loaded game list. If you do not want to do this, close the open game list or profile first.\nContinue loading games?",
                    "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Information )
                 == DialogResult.No ) {
                    return;
                }
            }

            GetStringDlg dlg = new GetStringDlg( "", "Download game list", "Enter custom URL name:", "Download game list" );
            if( dlg.ShowDialog() == DialogResult.OK ) {
                Cursor = Cursors.WaitCursor;
                try {
                    int loadedGames = gameData.LoadGameList( dlg.Value );
                    if( loadedGames == 0 ) {
                        MessageBox.Show( "No game data found. Please make sure the custom URL name is spelled correctly, and that the profile is public.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning );
                        AddStatus( "No games in download." );
                    } else {
                        MakeChange(true);
                        AddStatus( string.Format( "Downloaded {0} games.", loadedGames ) );
                        FillGameList();
                    }
                } catch( ApplicationException e ) {
                    MessageBox.Show( e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error );
                    AddStatus( "Error downloading games." );
                }
                Cursor = Cursors.Default;
            }
        }
        #endregion

        #region UI Updaters
        /// <summary>
        /// Completely re-populates the game list based on the current category selection.
        /// </summary>
        private void FillGameList() {
            lstGames.BeginUpdate();
            lstGames.Items.Clear();
            if( lstCategories.SelectedItems.Count > 0 ) {
                object catObj = lstCategories.SelectedItem;
                bool showAll = false;
                bool showFav = false;
                if( catObj is string ) {
                    if( (string)catObj == CAT_ALL_NAME ) {
                        showAll = true;
                    } else if( (string)catObj == CAT_FAV_NAME ) {
                        showFav = true;
                    }
                }
                Category cat = lstCategories.SelectedItem as Category;

                foreach( Game g in gameData.Games.Values ) {
                    if( showAll || ( showFav && g.Favorite ) || ( !showFav && g.Category == cat ) ) {
                        AddGameToList( g );
                    }
                }
                lstGames.Sort();
            }
            lstGames.EndUpdate();
            UpdateSelectedStatusText();
        }

        /// <summary>
        /// Adds an entry to the game list representing the given game.
        /// </summary>
        /// <param name="g">The game the new entry should represent.</param>
        private void AddGameToList( Game g ) {
            string catName = ( g.Category == null ) ? CAT_UNC_NAME : g.Category.Name;
            ListViewItem item = new ListViewItem( new string[] { g.Id.ToString(), g.Name, catName, g.Favorite ? "Y" : "N" } );
            item.Tag = g;
            lstGames.Items.Add( item );
        }

        /// <summary>
        /// Completely repopulates the category list and combobox. Maintains selection on both.
        /// </summary>
        private void FillCategoryList() {
            gameData.Categories.Sort();
            object[] catList = gameData.Categories.ToArray();

            lstCategories.BeginUpdate();
            object selected = lstCategories.SelectedItem;
            lstCategories.Items.Clear();
            lstCategories.Items.Add( CAT_ALL_NAME );
            lstCategories.Items.Add( CAT_FAV_NAME );
            lstCategories.Items.Add( CAT_UNC_NAME );
            lstCategories.Items.AddRange( catList );
            if( selected == null || !lstCategories.Items.Contains( selected ) ) {
                lstCategories.SelectedIndex = 0;
            } else {
                lstCategories.SelectedItem = selected;
            }
            lstCategories.EndUpdate();

            combCategory.BeginUpdate();
            selected = combCategory.SelectedItem;
            combCategory.Items.Clear();
            combCategory.Items.Add( COMBO_NEWCAT );
            combCategory.Items.Add( COMBO_NONE );
            combCategory.Items.Add( "" );
            combCategory.Items.AddRange( catList );
            combCategory.SelectedItem = selected;
            combCategory.EndUpdate();
        }

        /// <summary>
        /// Updates the game list sorter based on the current values of the sort settings fields.
        /// </summary>
        private void UpdateGameSorter() {
            lstGames.ListViewItemSorter = new GameListViewItemComparer( sortColumn, sortDirection, sortColumn == 0 );
        }

        /// <summary>
        /// Updates the entry for the game in the given position in the list.
        /// </summary>
        /// <param name="index">List index of the game to update</param>
        /// <returns>True if game should be in the list, false otherwise.</returns>
        bool UpdateGame( int index ) {
            ListViewItem item = lstGames.Items[index];
            Game g = (Game)item.Tag;
            if( ShouldDisplayGame( g ) ) {
                item.SubItems[2].Text = g.Category == null ? CAT_UNC_NAME : g.Category.Name;
                item.SubItems[3].Text = g.Favorite ? "Y" : "N";
                return true;
            } else {
                lstGames.Items.RemoveAt( index );
                return false;
            }
        }

        /// <summary>
        /// Updates list item for every game on the list, removing games that no longer need to be there, but not adding new ones.
        /// </summary>
        void UpdateGameList() {
            int i = 0;
            lstGames.BeginUpdate();
            while( i < lstGames.Items.Count ) {
                if( UpdateGame( i ) ) i++;
            }
            lstGames.EndUpdate();
            UpdateSelectedStatusText();
        }

        /// <summary>
        /// Updates the list item for every selected item on the list.
        /// </summary>
        void UpdateGameListSelected() {
            int i = 0;
            lstGames.BeginUpdate();
            while( i < lstGames.SelectedIndices.Count ) {
                if( UpdateGame( lstGames.SelectedIndices[i] ) ) i++;
            }
            lstGames.EndUpdate();
            UpdateSelectedStatusText();
        }

        /// <summary>
        /// Updates the text displaying the number of items in the game list
        /// </summary>
        private void UpdateSelectedStatusText() {
            statusSelection.Text = string.Format( "{0} selected / {1} displayed", lstGames.SelectedItems.Count, lstGames.Items.Count );
        }
        #endregion

        #region Data modifiers
        /// <summary>
        /// Creates a new category, first prompting the user for the name to use. If the name is not valid or in use, displays a notification.
        /// Also updates the UI, and selects the new category in the category combobox.
        /// </summary>
        /// <returns>The category that was added, or null if the operation was canceled or failed.</returns>
        public Category CreateCategory() {
            GetStringDlg dlg = new GetStringDlg( string.Empty, "Create category", "Enter new category name:", "Create" );
            if( dlg.ShowDialog() == DialogResult.OK && ValidateCategoryName( dlg.Value ) ) {
                Category newCat = gameData.AddCategory( dlg.Value );
                if( newCat != null ) {
                    FillCategoryList();
                    combCategory.SelectedItem = newCat;
                    MakeChange( true );
                    return newCat;
                } else {
                    MessageBox.Show( String.Format( "Could not add category '{0}'", dlg.Value ), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation );
                }
            }
            return null;
        }

        /// <summary>
        /// Deletes the given category and updates the UI. Prompts user for confirmation. Will completely rebuild the gamelist.
        /// </summary>
        /// <param name="c">Category to delete.</param>
        /// <returns>True if deletion occurred, false otherwise.</returns>
        public bool DeleteCategory( Category c ) {
            if( c != null ) {
                DialogResult res = MessageBox.Show( string.Format( "Delete category '{0}'?", c.Name ), "Confirm action", MessageBoxButtons.YesNo, MessageBoxIcon.Warning );
                if( res == System.Windows.Forms.DialogResult.Yes ) {
                    if( gameData.RemoveCategory( c ) ) {
                        FillCategoryList();
                        FillGameList();
                        MakeChange(true);
                        return true;
                    } else {
                        MessageBox.Show( string.Format( "Could not delete category '{0}'.", c.Name ), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation );
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Renames the given category. Prompts user for a new name. Updates UI. Will display an error if the rename fails.
        /// </summary>
        /// <param name="c">Category to rename</param>
        /// <returns>True if category was renamed, false otherwise.</returns>
        public bool RenameCategory( Category c ) {
            if( c != null ) {
                GetStringDlg dlg = new GetStringDlg( c.Name, string.Format( "Rename category: {0}", c.Name ), "Enter new name:", "Rename" );
                if( dlg.ShowDialog() == DialogResult.OK ) {
                    if( ValidateCategoryName( dlg.Value ) && gameData.RenameCategory( c, dlg.Value ) ) {
                        FillCategoryList();
                        UpdateGameList();
                        MakeChange(true);
                        return true;
                    } else {
                        MessageBox.Show( string.Format( "Name '{0}' is already in use.", dlg.Value ), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation );
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Assigns the given category to all selected items in the game list.
        /// </summary>
        /// <param name="cat">Category to assign</param>
        void AssignCategoryToSelectedGames( Category cat ) {
            if( lstGames.SelectedItems.Count > 0 ) {
                foreach( ListViewItem item in lstGames.SelectedItems ) {
                    ( item.Tag as Game ).Category = cat;
                }
                UpdateGameListSelected();
                MakeChange(true);
            }
        }

        /// <summary>
        /// Assigns the given favorite state to all selected items in the game list.
        /// </summary>
        /// <param name="fav">True to turn fav on, false to turn it off.</param>
        void AssignFavoriteToSelectedGames( bool fav ) {
            if( lstGames.SelectedItems.Count > 0 ) {
                foreach( ListViewItem item in lstGames.SelectedItems ) {
                    ( item.Tag as Game ).Favorite = fav;
                }
                UpdateGameListSelected();
                MakeChange(true);
            }
        }

        /// <summary>
        /// Unloads the current profile or game list, making sure the user gets the option to save any changes.
        /// </summary>
        /// <param name="updateUI">If true, will redraw the UI. Otherwise, will leave it up to the caller to do so.</param>
        /// <returns>True if there is now no loaded profile, false otherwise.</returns>
        void Unload( bool updateUI = true ) {
            if( !CheckForUnsaved() ) {
                return;
            }

            AddStatus( "Cleared data." );
            currentProfile = null;
            gameData = new GameData();
            MakeChange(false);
            UpdateMenuEnabledStates();
            if( updateUI ) {
                FillCategoryList();
                FillGameList();
            }
        }
        #endregion

        #region Utility
        /// <summary>
        /// Checks to see if a category name is valid. Does not make sure it isn't already in use. If the name is not valid, displays a warning.
        /// </summary>
        /// <param name="name">Name to check</param>
        /// <returns>True if valid, false otherwise</returns>
        private static bool ValidateCategoryName( string name ) {
            if( name == null || name == string.Empty ) {
                MessageBox.Show( "Category names cannot be empty.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation );
                return false;
            } else if( name == CAT_ALL_NAME || name == CAT_FAV_NAME || name == CAT_UNC_NAME ) {
                MessageBox.Show( string.Format( "Category name '{0}' is reserved.", name ), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation );
                return false;
            } else {
                return true;
            }
        }

        /// <summary>
        /// Gets the selected option on the favorite combo box.
        /// </summary>
        /// <returns>True if set to Yes, false otherwise.</returns>
        private bool GetSelectedFavorite() {
            return combFavorite.SelectedItem as string == "Yes";
        }

        /// <summary>
        /// Gets the selected category in the category combobox. Creates a new one, complete with user prompt and UI update, if necessary.
        /// </summary>
        /// <param name="result">The category created</param>
        /// <returns>True if there was anything selected at all, false otherwise</returns>
        public bool GetSelectedCategoryFromCombo( out Category result ) {
            result = null;
            if( combCategory.SelectedItem is Category ) {
                result = combCategory.SelectedItem as Category;
                return true;
            } else if( combCategory.SelectedItem is string ) {
                if( (string)combCategory.SelectedItem == COMBO_NEWCAT ) {
                    result = CreateCategory();
                    return result != null;
                } else if( (string)combCategory.SelectedItem == COMBO_NONE ) {
                    return true;
                }
            }
            return false;

        }

        /// <summary>
        /// Checks to see if a game should currently be displayed, based on the state of the category list.
        /// </summary>
        /// <param name="g">Game to check</param>
        /// <returns>True if it should be displayed, false otherwise</returns>
        private bool ShouldDisplayGame( Game g ) {
            if( lstCategories.SelectedItem == null ) {
                return false;
            }
            if( lstCategories.SelectedItem is string ) {
                if( (string)lstCategories.SelectedItem == CAT_ALL_NAME ) {
                    return true;
                }
                if( (string)lstCategories.SelectedItem == CAT_FAV_NAME ) {
                    return g.Favorite;
                }
                if( (string)lstCategories.SelectedItem == CAT_UNC_NAME ) {
                    return g.Category == null;
                }
            } else if( lstCategories.SelectedItem is Category ) {
                return g.Category == (Category)lstCategories.SelectedItem;
            }
            return false;
        }

        /// <summary>
        /// If there are any unsaved changes, asks the user if they want to save. Also gives the user the option to cancel the calling action.
        /// </summary>
        /// <returns>True if the action should proceed, false otherwise.</returns>
        private bool CheckForUnsaved() {
            if( !unsavedChanges ) {
                return true;
            }

            DialogResult res = MessageBox.Show( "Unsaved changes will be lost. Save first?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning );
            if( res == System.Windows.Forms.DialogResult.No ) {
                // Don't save, just continue
                return true;
            }
            if( res == System.Windows.Forms.DialogResult.Cancel ) {
                // Don't save, don't continue
                return false;
            }
            if( ProfileLoaded ) {
                try {
                    SaveProfile();
                    return true;
                } catch( ApplicationException e ) {
                    MessageBox.Show( "Saving profile data failed: " + e.Message, "Error saving file.", MessageBoxButtons.OK, MessageBoxIcon.Warning );
                    return false;
                }

            } else {
                return ManualExport();
            }
        }
        #endregion

        #region Profile Management

        /// <summary>
        /// Prompts user to create a new profile.
        /// </summary>
        private void CreateNewProfile() {
            ProfileDlg dlg = new ProfileDlg();
            DialogResult res = dlg.ShowDialog();
            if( res == System.Windows.Forms.DialogResult.OK ) {
                Cursor = Cursors.WaitCursor;
                currentProfile = dlg.Profile;
                gameData = currentProfile.GameData;
                AddStatus( "Profile created." );
                if( dlg.DownloadNow ) {
                    UpdateProfileDownload( false );
                }

                if( dlg.ImportNow ) {
                    UpdateProfileImport( false );
                }
                if( dlg.SetStartup ) {
                    settings.StartupAction = StartupAction.Load;
                    settings.ProfileToLoad = currentProfile.FilePath;
                    settings.Save();
                }

                FillCategoryList();
                FillGameList();
                Cursor = Cursors.Default;
            }
            UpdateMenuEnabledStates();
        }

        /// <summary>
        /// Prompts the user to modify the currently loaded profile.
        /// </summary>
        void EditProfile() {
            if( ProfileLoaded ) {
                ProfileDlg dlg = new ProfileDlg( currentProfile );
                if( dlg.ShowDialog() == DialogResult.OK ) {
                    AddStatus( "Profile edited." );
                    MakeChange(true);
                    bool refresh = false;
                    if( dlg.DownloadNow ) {
                        UpdateProfileDownload( false );
                        refresh = true;
                    }
                    if( dlg.ImportNow ) {
                        UpdateProfileImport( false );
                        refresh = true;
                    }
                    if( dlg.SetStartup ) {
                        settings.StartupAction = StartupAction.Load;
                        settings.ProfileToLoad = currentProfile.FilePath;
                        settings.Save();
                    }
                    if( refresh ) {
                        FillCategoryList();
                        FillGameList();
                    }
                }
            } else {
                if( MessageBox.Show( "No profile loaded. Create one now?", "Error", MessageBoxButtons.YesNo, MessageBoxIcon.Warning ) == DialogResult.Yes ) {
                    CreateNewProfile();
                }
            }
        }

        /// <summary>
        /// Prompts user for a profile file to load, then loads it.
        /// </summary>
        void LoadProfile() {
            if( !CheckForUnsaved() ) return;

            OpenFileDialog dlg = new OpenFileDialog();
            dlg.DefaultExt = "profile";
            dlg.AddExtension = true;
            dlg.CheckFileExists = true;
            dlg.Filter = "Profiles (*.profile)|*.profile";
            DialogResult res = dlg.ShowDialog();
            if( res == System.Windows.Forms.DialogResult.OK ) {
                LoadProfile( dlg.FileName, false );
            }
        }

        /// <summary>
        /// Loads the given profile file.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="checkForChanges"></param>
        void LoadProfile( string path, bool checkForChanges = true ) {
            if( checkForChanges && !CheckForUnsaved() ) return;

            try {
                currentProfile = ProfileData.Load( path );
                AddStatus( "Profile loaded." );
            } catch( ApplicationException e ) {
                MessageBox.Show( e.Message, "Error loading profile", MessageBoxButtons.OK, MessageBoxIcon.Warning );
                AddStatus( "Failed to load profile." );
                return;
            }

            gameData = currentProfile.GameData;

            if( currentProfile.AutoDownload ) {
                UpdateProfileDownload();
            }
            if( currentProfile.AutoImport ) {
                UpdateProfileImport();
            }

            FillCategoryList();
            FillGameList();
            UpdateMenuEnabledStates();
        }

        /// <summary>
        /// Prompts user for a file location and saves profile
        /// </summary>
        void SaveProfileAs() {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.DefaultExt = "profile";
            dlg.AddExtension = true;
            dlg.CheckPathExists = true;
            dlg.Filter = "Profiles (*.profile)|*.profile";
            DialogResult res = dlg.ShowDialog();
            if( res == System.Windows.Forms.DialogResult.OK ) {
                SaveProfile( dlg.FileName );
            }
        }

        /// <summary>
        /// Saves profile data to a file and performs any related tasks. This is the main saving function, all saves go through this function.
        /// </summary>
        /// <param name="path">Path to save to. If null, just saves profile to its current path.</param>
        /// <returns>True if successful, false if there is a failure</returns>
        bool SaveProfile( string path = null ) {
            if( currentProfile.AutoExport ) {
                ProfileExport();
            }
            try {
                if( path == null ) {
                    currentProfile.Save();
                } else {
                    currentProfile.Save( path );
                }
                AddStatus( "Profile saved." );
                MakeChange( false );
                return true;
            } catch( ApplicationException e ) {
                MessageBox.Show( e.Message, "Error saving profile", MessageBoxButtons.OK, MessageBoxIcon.Error );
                AddStatus( "Failed to save profile." );
                return false;
            }

        }

        /// <summary>
        /// Attempts to download game list for the loaded profile.
        /// </summary>
        /// <param name="updateUI">If true, will update the UI</param>
        void UpdateProfileDownload( bool updateUI = true ) {
            if( currentProfile != null ) {
                if( updateUI ) Cursor = Cursors.WaitCursor;
                try {
                    int count = currentProfile.DownloadGameList();
                    AddStatus( string.Format( "Downloaded {0} items.", count ) );
                    if( count > 0 ) {
                        MakeChange(true);
                        if( updateUI ) {
                            FillCategoryList();
                            FillGameList();
                        }
                    }
                    if( updateUI ) Cursor = Cursors.Default;
                } catch( ApplicationException e ) {
                    if( updateUI ) Cursor = Cursors.Default;
                    MessageBox.Show( e.Message, "Error downloading game list", MessageBoxButtons.OK, MessageBoxIcon.Warning );
                    AddStatus( "Download failed." );
                }
            }
        }

        /// <summary>
        /// Attempts to import steam categories
        /// </summary>
        /// <param name="updateUI"></param>
        void UpdateProfileImport( bool updateUI = true ) {
            if( currentProfile != null ) {
                if( updateUI ) Cursor = Cursors.WaitCursor;
                try {
                    int count = currentProfile.ImportSteamData();
                    AddStatus( string.Format( "Imported {0} items.", count ) );
                    if( count > 0 ) {
                        MakeChange(true);
                        if( updateUI ) {
                            FillCategoryList();
                            FillGameList();
                        }
                    }
                    if( updateUI ) Cursor = Cursors.Default;
                } catch( ApplicationException e ) {
                    if( updateUI ) Cursor = Cursors.Default;
                    MessageBox.Show( e.Message, "Error importing steam data list", MessageBoxButtons.OK, MessageBoxIcon.Warning );
                    AddStatus( "Import failed." );
                }
            }
        }

        /// <summary>
        /// Attempts to export steam categories
        /// </summary>
        void ProfileExport() {
            if( currentProfile != null ) {
                try {
                    currentProfile.ExportSteamData();
                    AddStatus( "Exported categories." );
                } catch( ApplicationException e ) {
                    MessageBox.Show( e.Message, "Error exporting to Steam", MessageBoxButtons.OK, MessageBoxIcon.Error );
                    AddStatus( "Export failed." );
                }
            }
        }

        #endregion

        #region UI Event Handlers
        #region Drag and drop

        private void lstCategories_DragEnter( object sender, DragEventArgs e ) {
            e.Effect = DragDropEffects.Move;
        }

        private void lstCategories_DragDrop( object sender, DragEventArgs e ) {
            if( e.Data.GetDataPresent( typeof( int[] ) ) ) {
                ClearStatus();
                Point clientPoint = lstCategories.PointToClient( new Point( e.X, e.Y ) );
                object dropItem = lstCategories.Items[lstCategories.IndexFromPoint( clientPoint )];
                if( dropItem is Category ) {
                    gameData.SetGameCategories( (int[])e.Data.GetData( typeof( int[] ) ), (Category)dropItem );
                    UpdateGameList();
                    MakeChange(true);
                } else if( dropItem is string ) {
                    if( (string)dropItem == CAT_FAV_NAME ) {
                        gameData.SetGameFavorites( (int[])e.Data.GetData( typeof( int[] ) ), true );
                        UpdateGameList();
                        MakeChange(true);
                    } else if( (string)dropItem == CAT_UNC_NAME ) {
                        gameData.SetGameCategories( (int[])e.Data.GetData( typeof( int[] ) ), null );
                        UpdateGameList();
                        MakeChange(true);
                    }
                }
                FlushStatus();
            }
        }

        private void lstGames_ItemDrag( object sender, ItemDragEventArgs e ) {
            int[] selectedGames = new int[lstGames.SelectedItems.Count];
            for( int i = 0; i < lstGames.SelectedItems.Count; i++ ) {
                selectedGames[i] = ( (Game)lstGames.SelectedItems[i].Tag ).Id;
            }
            lstGames.DoDragDrop( selectedGames, DragDropEffects.Move );
        }
        #endregion
        #region Main menu
        private void menu_File_NewProfile_Click( object sender, EventArgs e ) {
            ClearStatus();
            CreateNewProfile();
            FlushStatus();
        }

        private void menu_File_LoadProfile_Click( object sender, EventArgs e ) {
            ClearStatus();
            LoadProfile();
            FlushStatus();
        }

        private void menu_File_SaveProfile_Click( object sender, EventArgs e ) {
            ClearStatus();
            SaveProfile();
            FlushStatus();
        }

        private void menu_File_SaveProfileAs_Click( object sender, EventArgs e ) {
            ClearStatus();
            SaveProfileAs();
            FlushStatus();
        }

        private void menu_File_Close_Click( object sender, EventArgs e ) {
            ClearStatus();
            Unload();
            FlushStatus();
        }

        private void menu_File_Manual_Import_Click( object sender, EventArgs e ) {
            ClearStatus();
            ManualImport();
            FlushStatus();
        }

        private void menu_File_Manual_Download_Click( object sender, EventArgs e ) {
            ClearStatus();
            ManualDownload();
            FlushStatus();
        }

        private void menu_File_Manual_Export_Click( object sender, EventArgs e ) {
            ClearStatus();
            ManualExport();
            FlushStatus();
        }

        private void menu_File_Exit_Click( object sender, EventArgs e ) {
            this.Close();
        }

        private void menu_Profile_Download_Click( object sender, EventArgs e ) {
            ClearStatus();
            UpdateProfileDownload();
            FlushStatus();
        }

        private void menu_Profile_Import_Click( object sender, EventArgs e ) {
            ClearStatus();
            UpdateProfileImport();
            FlushStatus();
        }

        private void menu_Profile_Export_Click( object sender, EventArgs e ) {
            ClearStatus();
            ProfileExport();
            FlushStatus();
        }

        private void menu_Profile_Edit_Click( object sender, EventArgs e ) {
            ClearStatus();
            EditProfile();
            FlushStatus();
        }

        private void menu_Config_Settings_Click( object sender, EventArgs e ) {
            ClearStatus();
            OptionsDlg dlg = new OptionsDlg();
            dlg.ShowDialog();
            FlushStatus();
        }

        #endregion
        #region Buttons
        private void cmdCatAdd_Click( object sender, EventArgs e ) {
            ClearStatus();
            CreateCategory();
            FlushStatus();
        }

        private void cmdCatRename_Click( object sender, EventArgs e ) {
            if( lstCategories.SelectedItems.Count > 0 ) {
                ClearStatus();
                RenameCategory( lstCategories.SelectedItem as Category );
                FlushStatus();
            }
        }

        private void cmdCatDelete_Click( object sender, EventArgs e ) {
            if( lstCategories.SelectedItems.Count > 0 ) {
                ClearStatus();
                DeleteCategory( lstCategories.SelectedItem as Category );
                FlushStatus();
            }
        }

        private void cmdGameSetCategory_Click( object sender, EventArgs e ) {
            Category c;
            if( GetSelectedCategoryFromCombo( out c ) ) {
                ClearStatus();
                AssignCategoryToSelectedGames( c );
                FlushStatus();
            }
        }

        private void cmdGameSetFavorite_Click( object sender, EventArgs e ) {
            ClearStatus();
            AssignFavoriteToSelectedGames( GetSelectedFavorite() );
            FlushStatus();
        }
        #endregion

        private void lstCategories_SelectedIndexChanged( object sender, EventArgs e ) {
            if( lstCategories.SelectedItem != lastSelectedCat ) {
                FillGameList();
                lastSelectedCat = lstCategories.SelectedItem;
            }
        }

        private void lstGames_ColumnClick( object sender, ColumnClickEventArgs e ) {
            if( e.Column == this.sortColumn ) {
                this.sortDirection *= -1;
            } else {
                this.sortDirection = 1;
                this.sortColumn = e.Column;
            }
            UpdateGameSorter();
        }

        private void lstGames_SelectedIndexChanged( object sender, EventArgs e ) {
            UpdateSelectedStatusText();
        }

        private void FormMain_Shown( object sender, EventArgs e ) {
            ClearStatus();
            if( settings.SteamPath == null ) {
                SteamPathDlg dlg = new SteamPathDlg();
                dlg.ShowDialog();
                settings.SteamPath = dlg.Path;
                settings.Save();
            }
            switch( settings.StartupAction ) {
                case StartupAction.Load:
                    LoadProfile( settings.ProfileToLoad, false );
                    break;
                case StartupAction.Create:
                    CreateNewProfile();
                    break;
            }
            FlushStatus();
        }

        private void FormMain_FormClosing( object sender, FormClosingEventArgs e ) {
            if( e.CloseReason == CloseReason.UserClosing ) {
                e.Cancel = !CheckForUnsaved();
            }
        }
        #endregion

        void UpdateMenuEnabledStates() {
            bool enable = ProfileLoaded;
            menu_File_SaveProfile.Enabled = enable;
            menu_File_SaveProfileAs.Enabled = enable;

            menu_Profile_Download.Enabled = enable;
            menu_Profile_Export.Enabled = enable;
            menu_Profile_Import.Enabled = enable;
            menu_Profile_Edit.Enabled = enable;

            UpdateTitle();
        }

        void UpdateTitle() {
            StringBuilder sb = new StringBuilder( "Depressurizer" );
            if( ProfileLoaded ) {
                sb.Append( " - " );
                sb.Append( Path.GetFileName( currentProfile.FilePath ) );
            }
            if( unsavedChanges ) {
                sb.Append( " *" );
            }
            this.Text = sb.ToString();
        }
    }

    /// <summary>
    /// Implements the manual sorting of ListView items by columns. Supports sorting string representations of integers numerically.
    /// </summary>
    class GameListViewItemComparer : IComparer {
        private int col;
        private int direction;
        private bool asInt;
        public GameListViewItemComparer( int column = 0, int dir = 1, bool asInt = false ) {
            this.col = column;
            this.direction = dir;
            this.asInt = asInt;
        }

        public int Compare( object x, object y ) {
            if( asInt ) {
                int a, b;
                if( int.TryParse( ( (ListViewItem)x ).SubItems[col].Text, out a ) && int.TryParse( ( (ListViewItem)y ).SubItems[col].Text, out b ) ) {
                    return direction * ( a - b );
                }
            }
            return direction * String.Compare( ( (ListViewItem)x ).SubItems[col].Text, ( (ListViewItem)y ).SubItems[col].Text );
        }
    }
}
