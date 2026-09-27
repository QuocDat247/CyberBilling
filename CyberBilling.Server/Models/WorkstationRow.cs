using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CyberBilling.Server.Networking;

namespace CyberBilling.Server.Models;

public sealed class WorkstationRow :
    INotifyPropertyChanged
{
    private WorkstationConnectionState
        _connectionState;

    private string _status =
        "Sẵn sàng";

    private string _machineName =
        string.Empty;

    private string _startTimeText =
        "--";

    private string _usedTimeText =
        "--";

    private string _remainingTimeText =
        "--";

    private string _amountText =
        "0 đ";

    private string _startDateText =
        "--";

    private Brush _machineNameBackground =
        Brushes.Transparent;

    public WorkstationRow(
        int workstationNumber,
        string machineId,
        string machineName,
        WorkstationConnectionState connectionState)
    {
        WorkstationNumber =
            workstationNumber;

        MachineId =
            machineId;

        _machineName =
            machineName;

        _connectionState =
            connectionState;
    }

    public int WorkstationNumber
    {
        get;
    }

    public string WorkstationNumberText =>
        WorkstationNumber.ToString("00");

    public string MachineId
    {
        get;
    }

    public WorkstationConnectionState
        ConnectionState
    {
        get => _connectionState;

        set
        {
            if (_connectionState == value)
            {
                return;
            }

            _connectionState =
                value;

            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;

        set
        {
            if (_status == value)
            {
                return;
            }

            _status =
                value;

            OnPropertyChanged();
        }
    }

    public string MachineName
    {
        get => _machineName;

        set
        {
            if (_machineName == value)
            {
                return;
            }

            _machineName =
                value;

            OnPropertyChanged();
        }
    }

    public string StartTimeText
    {
        get => _startTimeText;

        set
        {
            if (_startTimeText == value)
            {
                return;
            }

            _startTimeText =
                value;

            OnPropertyChanged();
        }
    }

    public string UsedTimeText
    {
        get => _usedTimeText;

        set
        {
            if (_usedTimeText == value)
            {
                return;
            }

            _usedTimeText =
                value;

            OnPropertyChanged();
        }
    }

    public string RemainingTimeText
    {
        get => _remainingTimeText;

        set
        {
            if (_remainingTimeText == value)
            {
                return;
            }

            _remainingTimeText =
                value;

            OnPropertyChanged();
        }
    }

    public string AmountText
    {
        get => _amountText;

        set
        {
            if (_amountText == value)
            {
                return;
            }

            _amountText =
                value;

            OnPropertyChanged();
        }
    }

    public string StartDateText
    {
        get => _startDateText;

        set
        {
            if (_startDateText == value)
            {
                return;
            }

            _startDateText =
                value;

            OnPropertyChanged();
        }
    }

    public Brush MachineNameBackground
    {
        get => _machineNameBackground;

        set
        {
            if (Equals(
                    _machineNameBackground,
                    value))
            {
                return;
            }

            _machineNameBackground =
                value;

            OnPropertyChanged();
        }
    }

    public Brush MachineNameForeground =>
        Brushes.White;

    /*
     * PHẦN PHIÊN CHƠI.
     * Chưa lưu DB ở milestone này.
     */

    public bool IsSessionActive
    {
        get;
        set;
    }

    public DateTime? SessionStartedAt
    {
        get;
        set;
    }

    public decimal ServiceAmount
    {
        get;
        set;
    }

    public SessionBillingMode SessionMode
    {
        get;
        set;
    }

    public decimal PrepaidAmount
    {
        get;
        set;
    }

    public bool IsPrepaidExpired
    {
        get;
        set;
    }

    public decimal HourlyRate
    {
        get;
        set;
    }

    public event PropertyChangedEventHandler?
        PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}