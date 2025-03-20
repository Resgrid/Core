import {
  Component,
  ChangeDetectionStrategy,
  ViewChild,
  TemplateRef,
  OnInit,
  Input,
  ChangeDetectorRef,
} from '@angular/core';
import {
  ConnectionState,
  Consts,
  EventsService,
  GetMapDataAndMarkersResult, GetMapLayersResult,
  MapDataAndMarkersData,
  MappingService,
  RealtimeGeolocationService,
} from '@resgrid/ngx-resgridlib';
import { environment } from 'environments/environment';
import * as L from 'leaflet';
import { Observable } from 'rxjs';
import * as _ from 'lodash';
import {debounceTime, distinctUntilChanged, take} from "rxjs/operators";

@Component({
  templateUrl: './map.component.html',
  styleUrls: ['./map.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MapComponent implements OnInit {
  @ViewChild('map') mapContainer;
  @ViewChild('filterTextInput') filterTextInput;

  public map: any;
  public layerControl: any;
  public baseMaps: any;
  public markers: any[];
  public layers: any[];
  public showCalls: boolean = true;
  public showStations: boolean = true;
  public showUnits: boolean = true;
  public showPersonnel: boolean = true;
  public hideLabels: boolean = false;
  public filterText: string = '';
  public updateDate: string = '';
  private signalRStarted: boolean = false;

  @Input()
  public showbuttons: boolean;

  @Input()
  public mapheight: string;

  @Input()
  public departmentId: string;

  @Input()
  public leafletosmurl: string;

  @Input()
  public mapattribution: string;

  public constructor(
    private mapProvider: MappingService,
    private cdRef: ChangeDetectorRef,
    private realtimeGeolocationService: RealtimeGeolocationService,
    private events: EventsService,
    private consts: Consts
  ) {
    this.markers = [];
  }

  ngOnInit(): void {
    const date = new Date();
    this.updateDate = date.toString();

    this.mapProvider.getMapDataAndMarkers().pipe(take(1)).subscribe((data: GetMapDataAndMarkersResult) => {
      this.processMapData(data.Data);
    });

    if (typeof this.showbuttons === 'undefined') {
      this.showbuttons = false;
    }

    if (typeof this.mapheight === 'undefined') {
      this.mapheight = '600px';
    }

    this.cdRef.detectChanges();
  }

  ngAfterViewInit() {
    if (this.filterTextInput) {
      this.filterTextInput.valueChanges
        .pipe(debounceTime(500))
        .pipe(distinctUntilChanged())
        .subscribe((value) => {
          this.mapProvider.getMapDataAndMarkers().subscribe((data) => {
            this.processMapData(data.Data);
          });
        });
    }

    this.startSignalR();
  }

  ngOnChanges(): void {
    if (typeof this.showbuttons === 'undefined') {
      this.showbuttons = false;
    }

    if (typeof this.mapheight === 'undefined') {
      this.mapheight = '600px';
    }

    this.cdRef.detectChanges();
  }

  public changeHideLabels(event) {
    if (event && event.target) {
      var checked = event.target.checked;
      this.hideLabels = checked;

      this.mapProvider.getMapDataAndMarkers().subscribe((data) => {
        this.processMapData(data.Data);
      });
    }
  }

  public changeShowCalls(event) {
    if (event && event.target) {
      var checked = event.target.checked;
      this.showCalls = checked;

      this.mapProvider.getMapDataAndMarkers().subscribe((data) => {
        this.processMapData(data.Data);
      });
    }
  }

  public changeShowStations(event) {
    if (event && event.target) {
      var checked = event.target.checked;
      this.showStations = checked;

      this.mapProvider.getMapDataAndMarkers().subscribe((data) => {
        this.processMapData(data.Data);
      });
    }
  }

  public changeShowUnits(event) {
    if (event && event.target) {
      var checked = event.target.checked;
      this.showUnits = checked;

      this.mapProvider.getMapDataAndMarkers().subscribe((data) => {
        this.processMapData(data.Data);
      });
    }
  }

  public changeShowPeople(event) {
    if (event && event.target) {
      var checked = event.target.checked;
      this.showPersonnel = checked;

      this.mapProvider.getMapDataAndMarkers().subscribe((data) => {
        this.processMapData(data.Data);
      });
    }
  }

  private getMapLayers() {
    this.mapProvider
      .getMayLayers(0)
      .pipe(take(1))
      .subscribe((data: GetMapLayersResult) => {
        if (data && data.Data && data.Data.LayerJson) {
          this.clearLayers();

          var jsonData = JSON.parse(data.Data.LayerJson);
          if (jsonData) {
            jsonData.forEach((json) => {
              var myLayer = L.geoJSON(json, {
                //onEachFeature: this.colorlayer,
                style: {
                  color: json.features[0].properties.color,
                  opacity: 0.7,
                  fillColor: json.features[0].properties.color,
                  //fillColor: json.features[0].properties.fillColor,
                  fillOpacity: 0.1,
                },
              }); //.addData(json);//.addTo(this.map);
              this.layerControl.addOverlay(
                myLayer,
                json.features[0].properties.name
              );

              this.layers.push(myLayer);
              //myLayer.addData(json);
            });
          }
        }
      });
  }

  private clearLayers() {
    if (this.layers && this.layers.length > 0) {
      this.layers.forEach((layer) => {
        try {
          this.layerControl.removeLayer(layer);
          this.map.removeLayer(layer);
        } catch (error) {

        }
      });
      this.layers = [];
    } else {
      this.layers = [];
    }
  }

  public colorlayer(feature, layer) {
    layer.on('mouseover', function (e) {
      layer.setStyle({
        fillOpacity: 0.4,
      });
    });
    layer.on('mouseout', function (e) {
      layer.setStyle({
        fillOpacity: 0,
      });
    });
  }

  private processMapData(data: MapDataAndMarkersData) {
    if (data) {
      const date = new Date();
      this.updateDate = date.toString();
      this.clearLayers();

      var mapCenter = this.getMapCenter(data);

      if (!this.map) {
        var osm = L.tileLayer(
          this.leafletosmurl,
          {
            maxZoom: 19,
            attribution: this.mapattribution,
          }
        );

        this.baseMaps = {
          OpenStreetMap: osm,
        };

        this.map = L.map(this.mapContainer.nativeElement, {
          //dragging: false,
          doubleClickZoom: false,
          zoomControl: true,
          layers: [osm],
        });

        this.layerControl = L.control.layers(this.baseMaps).addTo(this.map);
      }

      //this.mapProvider.setMarkersForMap(this.map);

      //this.setMapBounds();

      //if (this.map) {
      this.map.setView(mapCenter, this.getMapZoomLevel(data));
      //}

      // clear map markers
      if (this.markers && this.markers.length >= 0) {
        for (var i = 0; i < this.markers.length; i++) {
          if (this.markers[i]) {
            this.map.removeLayer(this.markers[i]);
          }
        }

        this.markers = new Array<any>();
      }

      if (data.MapMakerInfos && data.MapMakerInfos.length > 0) {
        if (data && data.MapMakerInfos) {
          data.MapMakerInfos.forEach((markerInfo) => {
            if (
              this.filterText === '' ||
              markerInfo.Title.toLowerCase().indexOf(
                this.filterText.toLowerCase()
              ) >= 0
            ) {
              let markerTitle = '';
              let marker = null;
              if (!this.hideLabels) markerTitle = markerInfo.Title;

              if (
                (markerInfo.Type == 0 && this.showCalls) ||
                (markerInfo.Type == 1 && this.showUnits) ||
                (markerInfo.Type == 2 && this.showStations) ||
                (markerInfo.Type == 3 && this.showPersonnel)
              ) {
                if (!this.hideLabels) {
                  marker = L.marker(
                    [markerInfo.Latitude, markerInfo.Longitude],
                    {
                      icon: L.icon({
                        iconUrl:
                          '/images/Mapping/' + markerInfo.ImagePath + '.png',
                        iconSize: [32, 37],
                        iconAnchor: [16, 37],
                      }),
                      draggable: false,
                      title: markerTitle
                    }
                  ).bindTooltip(markerTitle, {
                      permanent: true,
                      direction: 'bottom',
                    }).addTo(this.map);

                    marker.elementId = markerInfo.Id;
                } else {
                  marker = L.marker(
                    [markerInfo.Latitude, markerInfo.Longitude],
                    {
                      icon: L.icon({
                        iconUrl:
                          '/images/Mapping/' + markerInfo.ImagePath + '.png',
                        iconSize: [32, 37],
                        iconAnchor: [16, 37],
                      }),
                      draggable: false,
                      title: markerTitle
                    }
                  ).addTo(this.map);

                  marker.elementId = markerInfo.Id;
                }
              }

              if (marker) {
                this.markers.push(marker);
              }
            }
          });
        }

        if (this.markers && this.markers.length > 0) {
          var group = L.featureGroup(this.markers);
          this.map.fitBounds(group.getBounds());
        }
      }
    }

    this.getMapLayers();
  }

  private getMapCenter(data: MapDataAndMarkersData) {
    return [data.CenterLat, data.CenterLon];
  }

  private getMapZoomLevel(data: MapDataAndMarkersData): any {
    return data.ZoomLevel;
  }

  public startSignalR() {
    if (!this.signalRStarted) {
      Object.defineProperty(WebSocket, 'OPEN', { value: 1, });
      this.realtimeGeolocationService.connectionState$.subscribe(
        (state: ConnectionState) => {
          if (state === ConnectionState.Disconnected) {
            //this.realtimeGeolocationService.restart(this.departmentId);
          }
        }
      );

      this.signalrInit();
      this.realtimeGeolocationService.start();

      this.signalRStarted = true;
    }
  }

  public stopSignalR() {
    this.realtimeGeolocationService.stop();
    this.signalRStarted = false;
  }

  private signalrInit() {
    this.events.subscribe(
      this.consts.SIGNALR_EVENTS.PERSONNEL_LOCATION_UPDATED,
      (data: any) => {
        console.log('person location updated event');
        if (data) {
          let personMarker = _.find(this.markers, ['elementId', `p${data.userId}`]);

          if (personMarker) {
            const date = new Date();
            this.updateDate = date.toString();

            personMarker.setLatLng([data.latitude, data.longitude]);
          }
        }
      }
    );
    this.events.subscribe(
      this.consts.SIGNALR_EVENTS.UNIT_LOCATION_UPDATED,
      (data: any) => {
        console.log('unit location updated event');
        if (data) {
          let unitMarker = _.find(this.markers, ['elementId', `u${data.unitId}`]);

          if (unitMarker) {
            const date = new Date();
            this.updateDate = date.toString();

            unitMarker.setLatLng([data.latitude, data.longitude]);
          }
        }
      }
    );
  }
}
