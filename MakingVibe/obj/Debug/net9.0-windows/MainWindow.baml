   M S B A M L   `   `   `  ����E  AMakingVibe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'clr-namespace:MakingVibe
MakingVibe  5   	       MakingVibe.MainWindow   5   	   W SPresentationCore, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35e aSystem.Windows.Controls.Ribbon, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089R NWindowsBase, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35^ ZWindowsFormsIntegration, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35H 9http://schemas.microsoft.com/winfx/2006/xaml/presentation      5   	   R NSystem.Xaml, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5   	   @d2http://schemas.microsoft.com/expression/blend/2008     5   	   Jmc;http://schemas.openxmlformats.org/markup-compatibility/2006     5   	   *localclr-namespace:MakingVibe     5   	     � Title$  Explorador de Archivos��5   	   $	��600��6(   $	��900��65   .��5	      �  � ColumnDefinitions 5
   
   �� 5      $	��300 �6   �� 5      $��* �6   .+�5   
   � $��0��6   ���#F0F0F0 � RowDefinitions 5      �� 5      $
��Auto �6    �� 5      $��* �6    .+�5      �� $��0��6   $��10q�6&   .+�5      �� $��Explorador de Archivos��6   $
��Bold"�6:   $��16(�6L   $��0,0,0,10q�6Z   �� 5      -   5         ��Name$ btnSelectRoot��5      $��Seleccionar Carpeta Raíz��60   $	��0,5q�6S   $��5q�6`   6l   5      R� 5      -   5         R�Name$ treeViewFiles��5      $��1��6.   $��10q�6;   $��1q�6G   ���#CCCCCC6[   5      � 5   
   $��1��6    5       �� 5!      $
��Auto �6    �� 5"      $��* �6    .+�5&      c� $��0��6   ���#E0E0E0.@�5'      �� -   5        $ btnCopy��5'      $��Copiar��6*   $��3q�6;   $	��8,3q�6F   ${�False��6j   �� 5(      -   5        $ btnCut��5(      $��Cortar��6)   $��3q�6:   $	��8,3q�6E   ${�False��6h   �� 5)      -   5        $ btnPaste��5)      $��Pegar��6+   $��3q�6;   $	��8,3q�6F   ${�False��6k   �� 5*      �� 5+      -   5        $ 	btnDelete��5+      $��Eliminar��6,   $��3q�6?   $	��8,3q�6J   ${�False��6p   �� 5,      -   5        $ 	btnRename��5,      $��	Renombrar��6,   $��3q�6@   $	��8,3q�6K   ${�False��6q   �� 5-      �� 5.      -   5        $ 
btnRefresh��5.      $��	Refrescar��6-   $��3q�6A   $	��8,3q�6L   6Z   5/      � 52      $��1��6   $��10q�6     53      �� 54      $
��Auto �6$   �� 55      $��* �6$   .+�58      �� -	   5         ��Name$ txtCurrentPath��58   )   $��0��6   $��Ruta actual: ��6A   $��SemiBold"�6V   $��0,0,0,10q�6l   �� 5:      -
   5         ��Name$ tabControlMain��5:   *   $��1��6   .@�5;      �� $��Vista Previa��6   .��5<      �� -   5         ��Name$ txtFileContent��5<   "    �� 
IsReadOnly$
 True��6:   	 �� AcceptsReturn$
	 True��6L   "
 �� VerticalScrollBarVisibility$

 Auto=�5=   "   $ �� HorizontalScrollBarVisibility$
 Auto=�6E   $��Consolas)�5>   "   5?      �� 5@      $��Seleccionados��6   .��5A      �� -   5         ��Name$ listViewSelected��5A   #    �� View 5B      �� 5C   "   .P�5D   &   �� $��Nombre��65    �� Width$	 200��6E    �� DisplayMemberBinding 6Q   �� *Name+�� 5E   &   $
��Ruta��65   $	 300��6C    6O   �� *Path+�� 5F   &   $
��Tipo��65   $	 100��6C    6O   �� *Type+5G   #   5I      5J      5K      5L      5M      5N      5O      