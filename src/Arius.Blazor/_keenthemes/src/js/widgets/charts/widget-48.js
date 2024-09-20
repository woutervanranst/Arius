"use strict";

// Class definition
var KTChartsWidget48 = function () {
    var chart = {
        self: null,
        rendered: false
    };


    // Private methods
    var initChart = function(chart) {
        var element = document.getElementById("kt_charts_widget_48");

        if (!element) {
            return;
        }

        var height = parseInt(KTUtil.css(element, 'height'));  
        var baseColor = KTUtil.getCssVariableValue('--bs-danger');
        var lightColor = KTUtil.getCssVariableValue('--bs-danger');

        var options = {
            series: [{
                name: 'Sales',
                data: [5, 5, 15, 15, 19, 16, 27, 24, 34, 25, 40, 30, 19, 17, 22, 10, 14, 14]
            }],
            chart: {
                fontFamily: 'inherit',
                type: 'area',
                height: height,
                toolbar: {
                    show: false
                }
            },             
            legend: {
                show: false
            },
            dataLabels: {
                enabled: false
            },
            fill: {
                type: "gradient",
                gradient: {
                    shadeIntensity: 1,
                    opacityFrom: 0.5,
                    opacityTo: 0,
                    stops: [0, 120, 50]
                }
            },
            stroke: {
                curve: 'smooth',
                show: true,
                width: 2,
                colors: [baseColor]
            },
            xaxis: {                 
                axisBorder: {
                    show: false,
                },
                axisTicks: {
                    show: false
                },
                labels: {
                    show: false
                },
                crosshairs: {
                    position: 'front',
                    stroke: {
                        color: baseColor,
                        width: 1,
                        dashArray: 3
                    }
                },
                tooltip: {
                    enabled: false,
                }
            },
            yaxis: {
                labels: {
                    show: false
                }
            },
            states: {
                normal: {
                    filter: {
                        type: 'none',
                        value: 0
                    }
                },
                hover: {
                    filter: {
                        type: 'none',
                        value: 0
                    }
                },
                active: {
                    allowMultipleDataPointsSelection: false,
                    filter: {
                        type: 'none',
                        value: 0
                    }
                }
            },
            tooltip: {
                enabled: false                
            },
            colors: [lightColor],
            grid: { 
                yaxis: {
                    lines: {
                        show: false
                    }
                }
            },
            markers: {
                strokeColor: baseColor,
                strokeWidth: 2
            }
        }; 

        chart.self = new ApexCharts(element, options);

        // Set timeout to properly get the parent elements width
        setTimeout(function() {
            chart.self.render();
            chart.rendered = true;
        }, 200);   
    }

    // Public methods
    return {
        init: function () {
            initChart(chart);

            // Update chart on theme mode change
            KTThemeMode.on("kt.thememode.change", function() {                
                if (chart.rendered) {
                    chart.self.destroy();
                }

                initChart(chart);
            });
        }   
    }
}();

// Webpack support
if (typeof module !== 'undefined') {
    window.KTChartsWidget48 = module.exports = KTChartsWidget48;
}