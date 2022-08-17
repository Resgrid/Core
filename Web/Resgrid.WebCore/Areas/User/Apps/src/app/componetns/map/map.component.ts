import {
  Component,
  ChangeDetectionStrategy,
  ViewChild,
  TemplateRef,
  OnInit,
  Input,
  ChangeDetectorRef,
} from '@angular/core';
import { MapDataAndMarkersData, MappingService } from '@resgrid/ngx-resgridlib';
import { environment } from 'src/environments/environment';
import * as L from "leaflet";

@Component({
  templateUrl: './map.component.html',
  styleUrls: ['./map.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MapComponent implements OnInit {
  @ViewChild("map") mapContainer;
  public map: any;
  public markers: any[];
  public showCalls: boolean = true;
  public showStations: boolean = true;
  public showUnits: boolean = true;
  public showPersonnel: boolean = true;

  @Input()
  public showbuttons: boolean;

  @Input()
  public mapheight: string;

  public constructor(private mapProvider: MappingService, private cdRef: ChangeDetectorRef) {
    this.markers = [];
  }

  ngOnInit(): void {
    this.mapProvider.getMapDataAndMarkers().subscribe((data) => { this.processMapData(data.Data); });

    if (typeof this.showbuttons === 'undefined') {
      this.showbuttons = false;
    }

    if (typeof this.mapheight === 'undefined') {
      this.mapheight = '600px';
    }

    this.cdRef.detectChanges();
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
 
  public changeShowCalls(event) {
    var checked = event.target.checked;
    this.showCalls = checked;

    this.mapProvider.getMapDataAndMarkers().subscribe((data) => { this.processMapData(data.Data); });
  }

  public changeShowStations(event) {
    var checked = event.target.checked;
    this.showStations = checked;

    this.mapProvider.getMapDataAndMarkers().subscribe((data) => { this.processMapData(data.Data); });
  }

  public changeShowUnits(event) {
    var checked = event.target.checked;
    this.showUnits = checked;

    this.mapProvider.getMapDataAndMarkers().subscribe((data) => { this.processMapData(data.Data); });
  }

  public changeShowPeople(event) {
    var checked = event.target.checked;
    this.showPersonnel = checked; 

    this.mapProvider.getMapDataAndMarkers().subscribe((data) => { this.processMapData(data.Data); });
  }

  private processMapData(data: MapDataAndMarkersData) {
    if (data) {
      var mapCenter = this.getMapCenter(data);

      if (!this.map) {
        this.map = L.map(this.mapContainer.nativeElement, {
          //dragging: false,
          doubleClickZoom: false,
          zoomControl: false,
        });

        const mapKey = window['rgOsmKey'];

        L.tileLayer("https://api.maptiler.com/maps/streets/{z}/{x}/{y}.png?key=" + mapKey, {
          attribution:
            '&copy; <a href="http://openstreetmap.org">OpenStreetMap</a> contributors <a href="http://creativecommons.org/licenses/by-sa/2.0/">CC-BY-SA</a>',
        }).addTo(this.map);
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

            if ((markerInfo.Type == 0 && this.showCalls) || 
                (markerInfo.Type == 1 && this.showUnits) ||
                (markerInfo.Type == 2 && this.showStations)) {
              let marker = L.marker([markerInfo.Latitude, markerInfo.Longitude], {
                icon: L.icon({
                  iconUrl: "/images/mapping/" + markerInfo.ImagePath + ".png",
                  iconSize: [32, 37],
                  iconAnchor: [16, 37],
                }),
                draggable: false,
                title: markerInfo.Title,
                //tooltip: markerInfo.Title,
              })
                .bindTooltip(markerInfo.Title, {
                  permanent: true,
                  direction: "bottom",
                })
                .addTo(this.map);

              this.markers.push(marker);
            }
          });
        }

        var group = L.featureGroup(this.markers);
        this.map.fitBounds(group.getBounds());
      }
    }
  }

  private getMapCenter(data: MapDataAndMarkersData) {
    return [data.CenterLat, data.CenterLon];
  }

  private getMapZoomLevel(data: MapDataAndMarkersData): any {
    return data.ZoomLevel;
  }
}
