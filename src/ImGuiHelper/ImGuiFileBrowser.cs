using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nes_emulator.src.ImGuiHelper
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Numerics;
	using ImGuiNET;

	[Flags]
	public enum ImGuiFileBrowserFlags : uint
	{
		SelectDirectory = 1 << 0,
		EnterNewFilename = 1 << 1,
		NoModal = 1 << 2,
		NoTitleBar = 1 << 3,
		NoStatusBar = 1 << 4,
		CloseOnEsc = 1 << 5,
		CreateNewDir = 1 << 6,
		MultipleSelection = 1 << 7,
		HideRegularFiles = 1 << 8,
		ConfirmOnEnter = 1 << 9,
		SkipItemsCausingError = 1 << 10,
		EditPathString = 1 << 11
	}

	public class ImGuiFileBrowser
	{
		private int width = 700, height = 450, posX = 0, posY = 0;
		private ImGuiFileBrowserFlags flags;
		private string title = "File Browser", statusStr = "";
		private string openLabel, openNewDirLabel;
		private bool shouldOpen = false, shouldClose = false, isOpened = false, isOk = false, isPosSet = false;
		private List<string> typeFilters = new List<string>();
		private int typeFilterIndex = 0;
		private bool hasAllFilter = false;
		private DirectoryInfo currentDirectory;
		private List<FileRecord> fileRecords = new List<FileRecord>();
		private HashSet<string> selectedFilenames = new HashSet<string>();
		private string customizedInputName = "";
		private string inputNameBuffer = "";
		private bool editDir = false, setFocusToEditDir = false;
		private string currDirBuffer = "";

		public ImGuiFileBrowser(ImGuiFileBrowserFlags flags = 0, string defaultDirectory = null)
		{
			this.flags = flags;
			currentDirectory = new DirectoryInfo(defaultDirectory ?? Directory.GetCurrentDirectory());
			SetTitle(title);
			SetDirectory(currentDirectory.FullName);
		}

		public void SetWindowPos(int x, int y) { posX = x; posY = y; isPosSet = true; }
		public void SetWindowSize(int w, int h) { width = w; height = h; }
		public void SetTitle(string t)
		{
			title = t;
			openLabel = $"{title}##filebrowser_{GetHashCode()}";
			openNewDirLabel = $"new dir##new_dir_{GetHashCode()}";
		}
		public void Open()
		{
			UpdateFileRecords();
			ClearSelected();
			statusStr = "";
			shouldOpen = true;
			shouldClose = false;
			if (flags.HasFlag(ImGuiFileBrowserFlags.EnterNewFilename) && !string.IsNullOrEmpty(customizedInputName))
			{
				inputNameBuffer = customizedInputName;
				selectedFilenames = new HashSet<string> { inputNameBuffer };
			}
		}
		public void Close()
		{
			ClearSelected();
			statusStr = "";
			shouldClose = true;
			shouldOpen = false;
		}
		public bool IsOpened() => isOpened;
		public void Display()
		{
			ImGui.PushID(this.GetHashCode());
			try
			{
				shouldOpen = shouldOpen && !shouldClose;
				if (shouldOpen)
				{
					ImGui.OpenPopup(openLabel);
				}
				isOpened = false;

				if (flags.HasFlag(ImGuiFileBrowserFlags.NoModal))
				{
					if (isPosSet) ImGui.SetNextWindowPos(new Vector2(posX, posY));
					ImGui.SetNextWindowSize(new Vector2(width, height));
				}
				else
				{
					if (isPosSet) ImGui.SetNextWindowPos(new Vector2(posX, posY), ImGuiCond.FirstUseEver);
					ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.FirstUseEver);
				}

				bool windowOpen = flags.HasFlag(ImGuiFileBrowserFlags.NoModal)
					? ImGui.BeginPopup(openLabel)
					: ImGui.BeginPopupModal(openLabel, ImGuiWindowFlags.None);

				if (!windowOpen)
					return;

				isOpened = true;
				try
				{
					// Directory navigation bar
					if (editDir) // Editable path string mode
					{
						if (setFocusToEditDir)
						{
							ImGui.SetKeyboardFocusHere();
						}
						ImGui.InputText("##directory", ref currDirBuffer, 256, ImGuiInputTextFlags.EnterReturnsTrue);
						if (!ImGui.IsItemActive() && !setFocusToEditDir) editDir = false;
						setFocusToEditDir = false;
						if (ImGui.IsItemDeactivatedAfterEdit())
						{
							if (Directory.Exists(currDirBuffer))
							{
								SetDirectory(currDirBuffer);
							}
							else
							{
								statusStr = $"[{currDirBuffer}] is not a valid directory";
							}
						}
					}
					else
					{
						// Render directory sections (breadcrumbs)
						string[] sections = currentDirectory.FullName.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
						string cumulativePath = "";
						for (int i = 0; i < sections.Length; i++)
						{
							if (i > 0) ImGui.SameLine();
							cumulativePath = Path.Combine(cumulativePath, sections[i]);
							if (ImGui.SmallButton(sections[i]))
							{
								SetDirectory(cumulativePath);
							}
						}

						if (flags.HasFlag(ImGuiFileBrowserFlags.EditPathString))
						{
							ImGui.SameLine();
							if (ImGui.SmallButton("#"))
							{
								currDirBuffer = currentDirectory.FullName;
								editDir = true;
								setFocusToEditDir = true;
							}
							else
							{
								if (ImGui.IsItemHovered())
									ImGui.SetTooltip("Edit the current path");
							}
						}
					}

					ImGui.SameLine();
					if (ImGui.SmallButton("*"))
					{
						UpdateFileRecords();
					}
					else if (ImGui.IsItemHovered())
					{
						ImGui.SetTooltip("Refresh");
					}

					if (flags.HasFlag(ImGuiFileBrowserFlags.CreateNewDir))
					{
						ImGui.SameLine();
						if (ImGui.SmallButton("+"))
						{
							ImGui.OpenPopup(openNewDirLabel);
						}
						else if (ImGui.IsItemHovered())
						{
							ImGui.SetTooltip("Create a new directory");
						}

						if (ImGui.BeginPopup(openNewDirLabel))
						{
							string newDirName = "";
							ImGui.InputText("name", ref newDirName, 128);
							ImGui.SameLine();
							if (ImGui.Button("ok") && !string.IsNullOrEmpty(newDirName))
							{
								try
								{
									Directory.CreateDirectory(Path.Combine(currentDirectory.FullName, newDirName));
									UpdateFileRecords();
								}
								catch
								{
									statusStr = $"failed to create {newDirName}";
								}
								ImGui.CloseCurrentPopup();
							}
							ImGui.EndPopup();
						}
					}

					// File list
					float reserveHeight = ImGui.GetFrameHeightWithSpacing();
					if (flags.HasFlag(ImGuiFileBrowserFlags.EnterNewFilename))
						reserveHeight += ImGui.GetFrameHeightWithSpacing();

					ImGui.BeginChild("ch", new Vector2(0, -reserveHeight));
					foreach (var rsc in fileRecords.ToList())
					{
						if (!rsc.IsDir && flags.HasFlag(ImGuiFileBrowserFlags.HideRegularFiles) && flags.HasFlag(ImGuiFileBrowserFlags.SelectDirectory))
							continue;
						if (!rsc.IsDir && !IsExtensionMatched(rsc.Extension))
							continue;

						bool selected = selectedFilenames.Contains(rsc.Name);
						if (ImGui.Selectable(rsc.ShowName, selected, ImGuiSelectableFlags.NoAutoClosePopups))
						{
							bool wantDir = flags.HasFlag(ImGuiFileBrowserFlags.SelectDirectory);
							if (rsc.Name == ".." || rsc.IsDir != wantDir)
							{
								// Navigation, not selection
								if (rsc.IsDir && rsc.Name == "..")
									SetDirectory(currentDirectory.Parent?.FullName ?? currentDirectory.FullName);
							}
							else
							{
								if (flags.HasFlag(ImGuiFileBrowserFlags.MultipleSelection))
								{
									if (!selected)
										selectedFilenames.Add(rsc.Name);
									else
										selectedFilenames.Remove(rsc.Name);
								}
								else
								{
									selectedFilenames = new HashSet<string> { rsc.Name };
								}
								if (flags.HasFlag(ImGuiFileBrowserFlags.EnterNewFilename))
									inputNameBuffer = rsc.Name;
							}
						}
						if (ImGui.IsMouseDoubleClicked(0) && ImGui.IsItemHovered())
						{
							if (rsc.IsDir)
								SetDirectory(Path.Combine(currentDirectory.FullName, rsc.Name));
							else if (!flags.HasFlag(ImGuiFileBrowserFlags.SelectDirectory))
							{
								selectedFilenames = new HashSet<string> { rsc.Name };
								isOk = true;
								ImGui.CloseCurrentPopup();
							}
						}
					}
					ImGui.EndChild();

					// New filename input
					if (flags.HasFlag(ImGuiFileBrowserFlags.EnterNewFilename))
					{
						ImGui.InputText("##inputname", ref inputNameBuffer, 256);
						if (!string.IsNullOrEmpty(inputNameBuffer))
							selectedFilenames = new HashSet<string> { inputNameBuffer };
					}

					// Ok/Cancel buttons
					if (!flags.HasFlag(ImGuiFileBrowserFlags.SelectDirectory))
					{
						if (ImGui.Button(" ok ") && selectedFilenames.Count > 0)
						{
							isOk = true;
							Close();
							ImGui.CloseCurrentPopup();
						}
					}
					else
					{
						if (ImGui.Button(" ok "))
						{
							isOk = true;
							Close();
							ImGui.CloseCurrentPopup();
						}
					}
					ImGui.SameLine();
					if (ImGui.Button("cancel") || shouldClose)
					{
						Close();
						ImGui.CloseCurrentPopup();
					}

					if (!string.IsNullOrEmpty(statusStr) && !flags.HasFlag(ImGuiFileBrowserFlags.NoStatusBar))
					{
						ImGui.SameLine();
						ImGui.Text(statusStr);
					}

					// Type filters
					if (typeFilters.Count > 0)
					{
						ImGui.SameLine();
						if (ImGui.BeginCombo("##type_filters", typeFilters[typeFilterIndex]))
						{
							for (int i = 0; i < typeFilters.Count; ++i)
							{
								bool selected = i == typeFilterIndex;
								if (ImGui.Selectable(typeFilters[i], selected) && !selected)
									typeFilterIndex = i;
							}
							ImGui.EndCombo();
						}
					}
				}
				finally
				{
					ImGui.EndPopup();
				}
			}
			finally
			{
				ImGui.PopID();
			}
		}

		public bool HasSelected() => isOk;
		public void SetDirectory(string dir)
		{
			try
			{
				currentDirectory = new DirectoryInfo(dir);
				UpdateFileRecords();
			}
			catch (Exception ex)
			{
				statusStr = "error: " + ex.Message;
			}
		}
		public string GetDirectory() => currentDirectory.FullName;
		public string GetSelected() => selectedFilenames.Count == 0 ? currentDirectory.FullName : Path.Combine(currentDirectory.FullName, selectedFilenames.First());
		public List<string> GetMultiSelected() =>
			selectedFilenames.Count == 0
				? new List<string> { currentDirectory.FullName }
				: selectedFilenames.Select(f => Path.Combine(currentDirectory.FullName, f)).ToList();
		public void ClearSelected()
		{
			selectedFilenames.Clear();
			if (flags.HasFlag(ImGuiFileBrowserFlags.EnterNewFilename))
				inputNameBuffer = "";
			isOk = false;
		}
		public void SetTypeFilters(IEnumerable<string> filters)
		{
			typeFilters = filters.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (typeFilters.Count > 1 && !typeFilters.Contains(".*"))
			{
				typeFilters.Insert(0, string.Join(",", typeFilters));
				hasAllFilter = true;
			}
			else hasAllFilter = false;
			typeFilterIndex = 0;
		}
		public void SetCurrentTypeFilterIndex(int index) => typeFilterIndex = index;
		public void SetInputName(string input)
		{
			if (!flags.HasFlag(ImGuiFileBrowserFlags.EnterNewFilename))
				throw new InvalidOperationException("SetInputName can only be called when EnterNewFilename is enabled");
			customizedInputName = input;
		}

		private void UpdateFileRecords()
		{
			fileRecords.Clear();
			fileRecords.Add(new FileRecord { IsDir = true, Name = "..", ShowName = "[D] ..", Extension = "" });

			try
			{
				foreach (var entry in currentDirectory.EnumerateFileSystemInfos())
				{
					bool isDir = (entry.Attributes & FileAttributes.Directory) != 0;
					string ext = isDir ? "" : Path.GetExtension(entry.Name);
					fileRecords.Add(new FileRecord
					{
						IsDir = isDir,
						Name = entry.Name,
						Extension = ext,
						ShowName = (isDir ? "[D] " : "[F] ") + entry.Name
					});
				}
				fileRecords = fileRecords.OrderBy(r => !r.IsDir).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
			}
			catch (Exception ex)
			{
				statusStr = "error reading directory: " + ex.Message;
			}
		}

		private bool IsExtensionMatched(string ext)
		{
			if (typeFilters.Count == 0) return true;
			if (typeFilterIndex >= typeFilters.Count) return true;
			if (hasAllFilter && typeFilterIndex == 0)
				return typeFilters.Skip(1).Any(f => f.Equals(ext, StringComparison.OrdinalIgnoreCase));
			if (typeFilters[typeFilterIndex] == ".*") return true;
			return ext.Equals(typeFilters[typeFilterIndex], StringComparison.OrdinalIgnoreCase);
		}

		private class FileRecord
		{
			public bool IsDir;
			public string Name;
			public string ShowName;
			public string Extension;
		}
	}
}
