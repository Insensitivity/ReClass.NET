using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Windows.Forms;
using ReClassNET.Extensions;
using ReClassNET.Nodes;
using ReClassNET.Util;

namespace ReClassNET.UI
{
	public partial class ClassNodeView : UserControl
	{
		/// <summary>A custom tree node for class nodes with hierarchical structure.</summary>
		private class ClassTreeNode : TreeNode, IDisposable
		{
			public ClassNode ClassNode { get; }

			private readonly ValueTypeWrapper<bool> enableHierarchyView;
			private readonly ValueTypeWrapper<bool> autoExpand;

			/// <summary>Constructor of the class.</summary>
			/// <param name="node">The class node.</param>
			/// <param name="enableHierarchyView">The value if the hierarchy view is enabled.</param>
			/// <param name="autoExpand">The value if nodes should get expanded.</param>
			public ClassTreeNode(ClassNode node, ValueTypeWrapper<bool> enableHierarchyView, ValueTypeWrapper<bool> autoExpand)
				: this(node, enableHierarchyView, autoExpand, null)
			{
				Contract.Requires(node != null);
				Contract.Requires(enableHierarchyView != null);
				Contract.Requires(autoExpand != null);
			}

			private ClassTreeNode(ClassNode node, ValueTypeWrapper<bool> enableHierarchyView, ValueTypeWrapper<bool> autoExpand, HashSet<ClassNode> seen)
			{
				Contract.Requires(node != null);
				Contract.Requires(enableHierarchyView != null);
				Contract.Requires(autoExpand != null);

				this.enableHierarchyView = enableHierarchyView;
				this.autoExpand = autoExpand;

				ClassNode = node;

				node.NameChanged += NameChanged_Handler;
				node.NodesChanged += NodesChanged_Handler;

				Text = node.Name;

				ImageIndex = 1;
				SelectedImageIndex = 1;

				RebuildClassHierarchy(seen ?? new HashSet<ClassNode> { ClassNode });
			}

			public void Dispose()
			{
				ClassNode.NameChanged -= NameChanged_Handler;
				ClassNode.NodesChanged -= NodesChanged_Handler;

				Nodes.Cast<ClassTreeNode>().ForEach(t => t.Dispose());
				Nodes.Clear();
			}

			private void NameChanged_Handler(BaseNode sender)
			{
				Text = sender.Name;
			}

			private void NodesChanged_Handler(BaseNode sender)
			{
				RebuildClassHierarchy(new HashSet<ClassNode> { ClassNode });
			}

			/// <summary>Rebuilds the class hierarchy.</summary>
			/// <param name="seen">The already seen classes.</param>
			private void RebuildClassHierarchy(HashSet<ClassNode> seen)
			{
				Contract.Requires(seen != null);

				if (!enableHierarchyView.Value)
				{
					return;
				}

				var distinctClasses = ClassNode.Nodes
					.OfType<BaseReferenceNode>()
					.Select(r => r.InnerNode)
					.Distinct()
					.ToList();

				if (distinctClasses.SequenceEqualsEx(Nodes.Cast<ClassTreeNode>().Select(t => t.ClassNode)))
				{
					return;
				}

				Nodes.Cast<ClassTreeNode>().ForEach(t => t.Dispose());
				Nodes.Clear();

				foreach (var child in distinctClasses)
				{
					var childSeen = new HashSet<ClassNode>(seen);
					if (childSeen.Add(child))
					{
						Nodes.Add(new ClassTreeNode(child, enableHierarchyView, autoExpand, childSeen));
					}
				}

				if (autoExpand.Value)
				{
					Expand();
				}
			}
		}

		private readonly TreeNode root;
		private readonly ValueTypeWrapper<bool> enableHierarchyView;
		private readonly ValueTypeWrapper<bool> autoExpand;

		private ReClassNetProject project;

		private ClassNode selectedClass;

		public delegate void SelectionChangedEvent(object sender, ClassNode node);
		public event SelectionChangedEvent SelectionChanged;

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ReClassNetProject Project
		{
			get => project;
			set
			{
				Contract.Requires(value != null);

				if (project != value)
				{
					root.Nodes.Cast<ClassTreeNode>().ForEach(t => t.Dispose());
					root.Nodes.Clear();

					if (project != null)
					{
						project.ClassAdded -= AddClass;
						project.ClassRemoved -= RemoveClass;
					}

					project = value;

					project.ClassAdded += AddClass;
					project.ClassRemoved += RemoveClass;

					classesTreeView.BeginUpdate();

					project.Classes.ForEach(AddClassInternal);

					classesTreeView.Sort();

					classesTreeView.EndUpdate();
				}
			}
		}

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ClassNode SelectedClass
		{
			get => selectedClass;
			set
			{
				if (selectedClass != value)
				{
					selectedClass = value;
					if (selectedClass != null)
					{
						classesTreeView.SelectedNode = root.Nodes.Cast<ClassTreeNode>().FirstOrDefault(n => n.ClassNode == selectedClass);
					}

					SelectionChanged?.Invoke(this, selectedClass);
				}
			}
		}

		public ClassNodeView()
		{
			Contract.Ensures(root != null);

			InitializeComponent();

			DoubleBuffered = true;

			enableHierarchyView = new ValueTypeWrapper<bool>(false);
			autoExpand = new ValueTypeWrapper<bool>(false);

			classesTreeView.ImageList = new ImageList();
			classesTreeView.ImageList.Images.Add(Properties.Resources.B16x16_Text_List_Bullets);
			classesTreeView.ImageList.Images.Add(Properties.Resources.B16x16_Class_Type);

			root = new TreeNode
			{
				Text = "Classes",
				ImageIndex = 0,
				SelectedImageIndex = 0
			};

			classesTreeView.Nodes.Add(root);
		}

		#region Event Handler

		private void classesTreeView_AfterSelect(object sender, TreeViewEventArgs e)
		{
			if (e.Node.Level == 0)
			{
				return;
			}

			if (!(e.Node is ClassTreeNode node))
			{
				return;
			}

			if (selectedClass != node.ClassNode)
			{
				SelectedClass = node.ClassNode;
			}
		}

		private void classesTreeView_MouseUp(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Right)
			{
				var node = classesTreeView.GetNodeAt(e.X, e.Y);

				if (node != null)
				{
					if (node != root)
					{
						classesTreeView.SelectedNode = node;

						classContextMenuStrip.Show(classesTreeView, e.Location);
					}
					else
					{
						rootContextMenuStrip.Show(classesTreeView, e.Location);
					}
				}
			}
		}

		private void removeUnusedClassesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			project.RemoveUnusedClasses();
		}

		private void deleteClassToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (classesTreeView.SelectedNode is ClassTreeNode treeNode)
			{
				try
				{
					project.Remove(treeNode.ClassNode);
				}
				catch (ClassReferencedException ex)
				{
					Program.Logger.Log(ex);
				}
			}
		}

		private void renameClassToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var treeNode = classesTreeView.SelectedNode;
			treeNode?.BeginEdit();
		}

		private void classesTreeView_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
		{
			if (!string.IsNullOrEmpty(e.Label))
			{
				if (e.Node is ClassTreeNode node)
				{
					node.ClassNode.Name = e.Label;

					// Cancel the edit if the class refused the name.
					// This prevents the tree node from using the wrong name.
					if (node.ClassNode.Name != e.Label)
					{
						e.CancelEdit = true;
					}
				}
			}
		}

		private void enableHierarchyViewToolStripMenuItem_Click(object sender, EventArgs e)
		{
			enableHierarchyViewToolStripMenuItem.Checked = !enableHierarchyViewToolStripMenuItem.Checked;

			expandAllClassesToolStripMenuItem.Enabled =
				collapseAllClassesToolStripMenuItem.Enabled = enableHierarchyViewToolStripMenuItem.Checked;

			enableHierarchyView.Value = enableHierarchyViewToolStripMenuItem.Checked;

			var classes = root.Nodes.Cast<ClassTreeNode>().Select(t => t.ClassNode).ToList();

			root.Nodes.Cast<ClassTreeNode>().ForEach(t => t.Dispose());
			root.Nodes.Clear();

			classes.ForEach(AddClass);
		}

		private void autoExpandHierarchyViewToolStripMenuItem_Click(object sender, EventArgs e)
		{
			autoExpandHierarchyViewToolStripMenuItem.Checked = !autoExpandHierarchyViewToolStripMenuItem.Checked;

			autoExpand.Value = autoExpandHierarchyViewToolStripMenuItem.Checked;

			if (autoExpand.Value)
			{
				root.ExpandAll();
			}
		}

		private void expandAllClassesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			root.ExpandAll();
		}

		private void collapseAllClassesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			root.Nodes.Cast<TreeNode>().ForEach(n => n.Collapse());
		}

		private void addNewClassToolStripMenuItem_Click(object sender, EventArgs e)
		{
			LinkedWindowFeatures.CreateDefaultClass();
		}

		private void showCodeOfClassToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (classesTreeView.SelectedNode is ClassTreeNode node)
			{
				LinkedWindowFeatures.ShowCodeGeneratorForm(node.ClassNode.Yield());
			}
		}

		#endregion

		/// <summary>Adds the class to the view.</summary>
		/// <param name="node">The class to add.</param>
		public void AddClass(ClassNode node)
		{
			Contract.Requires(node != null);

			AddClassInternal(node);

			classesTreeView.Sort();
		}

		/// <summary>Removes the class from the view.</summary>
		/// <param name="node">The class to remove.</param>
		public void RemoveClass(ClassNode node)
		{
			var tn = FindClassTreeNode(node);
			if (tn == null)
			{
				return;
			}

				root.Nodes.Remove(tn);

				tn.Dispose();

				if (selectedClass == node)
				{
					if (root.Nodes.Count > 0)
					{
						classesTreeView.SelectedNode = root.Nodes[0];
					}
					else
					{
						SelectedClass = null;
					}
				}
			}

		/// <summary>
		/// Adds a new <see cref="ClassTreeNode"/> to the tree.
		/// </summary>
		/// <param name="node">The class to add.</param>
		private void AddClassInternal(ClassNode node)
		{
			Contract.Requires(node != null);

			root.Nodes.Add(new ClassTreeNode(node, enableHierarchyView, autoExpand));

			root.Expand();
		}

		/// <summary>Searches for the ClassTreeNode which represents the class.</summary>
		/// <param name="node">The class to search.</param>
		/// <returns>The found class tree node.</returns>
		private ClassTreeNode FindClassTreeNode(ClassNode node)
		{
			return root.Nodes.Cast<ClassTreeNode>().FirstOrDefault(t => t.ClassNode == node);
		}
	}
}
