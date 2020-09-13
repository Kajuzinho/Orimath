﻿using Orimath.Plugins;
using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;

namespace Orimath.UITest.ViewModels
{
    public class ControlListViewModel
    {
        public Type[] ControlTypes { get; }

        public ICommand CloseCommand { get; }

        public ControlListViewModel(IMessenger messenger)
        {
            ControlTypes = typeof(Control).Assembly
                .GetTypes()
                .Where(x => typeof(Control).IsAssignableFrom(x) && !x.IsAbstract)
                .ToArray();

            CloseCommand = messenger.CloseDialogCommand;
        }
    }
}