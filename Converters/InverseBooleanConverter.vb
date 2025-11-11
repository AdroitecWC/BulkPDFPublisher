Imports System
Imports System.Globalization
Imports System.Windows.Data

Namespace Converters
    Public Class InverseBooleanConverter
        Implements IValueConverter

        Public Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
            If TypeOf value Is Boolean Then
                Return Not CType(value, Boolean)
            End If
            Return value
        End Function

        Public Function ConvertBack(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
            If TypeOf value Is Boolean Then
                Return Not CType(value, Boolean)
            End If
            Return value
        End Function
    End Class
End Namespace
